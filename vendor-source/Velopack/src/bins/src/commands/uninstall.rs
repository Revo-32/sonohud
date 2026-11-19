// Adapted from Velopack 1.2.161 (MIT). SonoHUD contributors, 2026.
use crate::{dialogs, shared, windows};
use anyhow::{bail, ensure, Context, Result};
use std::{collections::{BTreeMap,BTreeSet}, fs::{self,File}, io::{self,Read}, os::windows::{ffi::OsStrExt,fs::MetadataExt},
    path::{Component,Path,PathBuf}, ffi::c_void};
use velopack::{bundle, constants, locator::VelopackLocator};
use winreg::{RegKey,enums::*};

unsafe extern "C" {
    fn sonohud_start_cleanup(up:*const u16,dir:*const u16,bytes:*const u8,size:u32,commit:*mut *mut c_void)->u32;
    fn sonohud_commit_cleanup(commit:*mut c_void);
    fn sonohud_cancel_cleanup(commit:*mut c_void);
}
struct DelayedCleanup(*mut c_void);
impl DelayedCleanup {
    fn start(root:&Path, up:&Path)->Result<Self>{
        let wide=|p:&Path| p.as_os_str().encode_wide().chain(Some(0)).collect::<Vec<_>>();
        let module=include_bytes!(concat!(env!("OUT_DIR"),"/SonoHudCleanup.dll"));
        let mut handle=std::ptr::null_mut();
        let code=unsafe{sonohud_start_cleanup(wide(up).as_ptr(),wide(root).as_ptr(),module.as_ptr(),module.len() as u32,&mut handle)};
        ensure!(code==0 && !handle.is_null(),"Windows could not prepare the uninstall worker ({}). No files have been removed.",code);
        Ok(Self(handle))
    }
    fn commit(mut self){unsafe{sonohud_commit_cleanup(self.0)}; self.0=std::ptr::null_mut();}
}
impl Drop for DelayedCleanup{fn drop(&mut self){if !self.0.is_null(){unsafe{sonohud_cancel_cleanup(self.0)}}}}

fn ordinary(root:&Path, file:&Path)->Result<bool>{
    let relative=file.strip_prefix(root)?;
    let mut path=root.to_path_buf();
    for term in relative.components(){
        let Component::Normal(name)=term else{bail!("Invalid package path");};
        path.push(name);
        match fs::symlink_metadata(&path){
            Ok(attr) if attr.file_attributes()&0x400!=0=>return Ok(false),
            Ok(_)=>{},
            Err(e) if e.kind()==io::ErrorKind::NotFound=>return Ok(false),
            Err(e)=>return Err(e.into())
        }
    } Ok(true)
}
type FileProof = BTreeMap<PathBuf,String>;
fn fingerprint(hash:sha2::Sha256)->String{use sha2::Digest;hash.finalize().iter().map(|b|format!("{b:02x}")).collect()}
fn disk_digest(p:&Path)->Result<String>{use sha2::{Digest,Sha256};let mut hash=Sha256::new();let mut file=File::open(p)?;let mut buf=[0;32768];loop{let n=file.read(&mut buf)?;if n==0{break;}hash.update(&buf[..n]);}Ok(fingerprint(hash))}
fn same_bytes(read:&mut impl Read, installed:&Path, size:u64)->Result<Option<String>>{
    use sha2::{Digest,Sha256};let mut hash=Sha256::new();
    let mut disk=File::open(installed)?;
    if disk.metadata()?.len()!=size{return Ok(None);}
    let mut left=[0;32768];let mut right=[0;32768];
    loop{
        let count=read.read(&mut left)?;
        if count==0{return Ok(Some(fingerprint(hash)));}
        disk.read_exact(&mut right[..count])?;
        if left[..count]!=right[..count]{return Ok(None);}hash.update(&left[..count]);
    }
}
fn owned_target(name:&str)->Result<Option<PathBuf>>{
    if name.ends_with('/') || name.ends_with('\\'){return Ok(None);}
    let name=name.replace('\\',"/");
    let terms:Vec<_>=name.split('/').collect();
    ensure!(terms.iter().all(|s|!s.is_empty() && *s!="." && *s!=".." && !s.contains(':') && !s.ends_with([' ','.'])),"Unsafe package entry");
    let leaf=*terms.last().unwrap();
    if leaf.eq_ignore_ascii_case("Squirrel.exe"){return Ok(None);}
    if let Some(stem)=leaf.strip_suffix("_ExecutionStub.exe"){return Ok(Some(PathBuf::from(format!("{}.exe",stem))));}
    if terms.len()>2 && terms[0]=="lib" {
        ensure!(!leaf.ends_with(".__symlink"),"The Core package does not use symlinks");
        return Ok(Some(Path::new("current").join(terms[2..].join("/"))));
    }
    if terms.len()==1 && leaf.ends_with(".nuspec"){return Ok(Some(PathBuf::from("current/sq.version")));}
    Ok(None)
}
fn check_location(locator:&VelopackLocator)->Result<()>{
    let root=locator.get_root_dir();let up=locator.get_update_path();
    ensure!(root.is_absolute() && root.parent().and_then(Path::parent).is_some() &&
            fs::symlink_metadata(&root)?.file_attributes()&0x400==0,"Unexpected installation root");
    ensure!(same_file::is_same_file(&up,std::env::current_exe()?)? && up==root.join("Update.exe"),"The running updater does not own this directory");
    ensure!(locator.get_current_bin_dir()==root.join("current") && locator.get_packages_dir()==root.join("packages"),"Unsupported installation layout");
    ensure!(ordinary(&root,&locator.get_current_bin_dir())? && ordinary(&root,&locator.get_packages_dir())? && ordinary(&root,&up)?,"Reparse points are not valid product roots");
    let key=RegKey::predef(HKEY_CURRENT_USER).open_subkey(format!(r"Software\Microsoft\Windows\CurrentVersion\Uninstall\{}",locator.get_manifest_id()))?;
    let registered:String=key.get_value("InstallLocation")?;
    ensure!(same_file::is_same_file(&root,registered)?,"Windows registration belongs to a different installation");
    Ok(())
}
fn inventory(locator:&VelopackLocator)->Result<(Vec<PathBuf>,Vec<PathBuf>,FileProof)>{
    let root=locator.get_root_dir();let mut packages=Vec::new();let mut source=None;
    for item in fs::read_dir(locator.get_packages_dir())?{
        let p=item?.path();
        if !p.is_file() || !ordinary(&root,&p)?{continue;}
        let Some(name)=p.file_name().and_then(|x|x.to_str()) else{continue};
        let id=locator.get_manifest_id();
        if !name.starts_with(&format!("{}-",id)) || !name.ends_with(".nupkg"){continue;}
        let Ok(mut package)=bundle::load_bundle_from_file(&p) else{continue};
        let Ok(meta)=package.read_manifest() else{continue};
        if meta.id!=id{continue;}
        // A feed name includes a channel. Its real version is in the nuspec.
        let channel=if meta.channel.is_empty(){locator.get_manifest_channel()}else{meta.channel};
        let base=format!("{}-{}",id,meta.version);
        let form=|kind:&str|name.eq_ignore_ascii_case(&format!("{}-{}.nupkg",base,kind)) ||
            name.eq_ignore_ascii_case(&format!("{}-{}-{}.nupkg",base,channel,kind));
        if !form("full") && !form("delta"){continue;}
        if form("full") && meta.version==locator.get_manifest_version(){source=Some(p.clone());}
        packages.push(p);
    }
    let mut files=BTreeSet::new();let mut dirs=BTreeSet::new();let mut proof=FileProof::new();
    if let Some(package)=source{
        let mut zip=zip::ZipArchive::new(File::open(package)?)?;
        for i in 0..zip.len(){
            let mut entry=zip.by_index(i)?;
            let Some(rel)=owned_target(entry.name())? else{continue};
            let target=root.join(rel);
            if !ordinary(&root,&target)? || !target.is_file(){continue;}
            let size=entry.size();
            if let Some(sha)=same_bytes(&mut entry,&target,size)?{
                if let Ok(r)=target.strip_prefix(locator.get_current_bin_dir()){proof.insert(r.to_owned(),sha);}
                files.insert(target);
            }
            else{warn!("Preserving locally changed file {:?}",target);}
        }
    }else{
        ensure!(locator.get_manifest_id()=="SonoHUD.Core" && locator.get_manifest_version_full_string()=="0.7.2",
            "Installed version package unavailable. Installation was retained.");
        // Velopack 1.2.161 may have replaced the updater and purged the old full
        // before the old app ever ran this release. Match only the frozen 0.7.2 bytes.
        for line in include_str!("../../sonohud-legacy-owned.tsv").lines().filter(|l|!l.starts_with('#')){
            let mut fields=line.splitn(3,'\t');
            let digest=fields.next().context("Legacy digest missing")?;
            let size:u64=fields.next().context("Legacy size missing")?.parse()?;
            let target=root.join(fields.next().context("Legacy filename missing")?);
            if !ordinary(&root,&target)? || !target.is_file() || fs::metadata(&target)?.len()!=size{continue;}
            if disk_digest(&target)?==digest{
                if let Ok(r)=target.strip_prefix(locator.get_current_bin_dir()){proof.insert(r.to_owned(),digest.to_owned());}
                files.insert(target);
            }
        }
    }
    for file in &files{
        let mut parent=file.parent();
        while let Some(p)=parent{if p==root{break;}dirs.insert(p.to_owned());parent=p.parent();}
    }
    files.extend(packages);
    let beta=locator.get_packages_dir().join(".betaId");
    if ordinary(&root,&beta)? && fs::read_to_string(&beta).ok().is_some_and(|s|{
        let id=s.strip_prefix('\u{feff}').unwrap_or(&s);
        (id.len()==32 && id.bytes().all(|b|b.is_ascii_hexdigit())) ||
        (id.len()==36 && id.bytes().enumerate().all(|(i,b)|if [8,13,18,23].contains(&i){b==b'-'}else{b.is_ascii_hexdigit()}))
    }){files.insert(beta);}
    dirs.insert(locator.get_temp_dir_root());dirs.insert(locator.get_packages_dir());dirs.insert(locator.get_current_bin_dir());
    let mut ordered:Vec<_>=files.into_iter().collect();
    ordered.sort_by_key(|f| if f.starts_with(locator.get_packages_dir()){2}else if f==&root.join("current/sq.version"){1}else{0});
    Ok((ordered,dirs.into_iter().rev().collect(),proof))
}


/// Only a byte-for-byte identified current version may be replaced/deleted.
pub(crate) struct UpdateOwnership(FileProof);
impl UpdateOwnership {
    pub(crate) fn load(locator:&VelopackLocator)->Result<Self>{
        let (_,_,sealed)=inventory(locator)?;
        let proof=Self(sealed);proof.verify(&locator.get_current_bin_dir())?;Ok(proof)
    }
    pub(crate) fn verify(&self,dir:&Path)->Result<()>{
        for item in walkdir::WalkDir::new(dir).follow_links(false){
            let item=item?;let p=item.path();let rel=p.strip_prefix(dir)?;
            let meta=fs::symlink_metadata(p)?;
            let known=if meta.file_attributes()&0x400!=0{false}
                else if meta.is_dir(){rel.as_os_str().is_empty() || self.0.keys().any(|r|r.starts_with(rel))}
                else if let Some(hash)=self.0.get(rel){disk_digest(p)?==*hash}else{false};
            ensure!(known,"Unrecognized or modified file in current: {:?}. Move it to a user folder or restore the original file, then retry the update.",rel);
        }
        Ok(())
    }
    pub(crate) fn remove_verified_backup(&self,dir:&Path){
        if !dir.exists() || fs::symlink_metadata(dir).map_or(true,|m|m.file_attributes()&0x400!=0){return;}
        let mut folders=BTreeSet::new();
        for (rel,digest) in &self.0{
            let target=dir.join(rel);
            if ordinary(dir,&target).unwrap_or(false) && disk_digest(&target).ok().as_ref()==Some(digest){
                let _=fs::remove_file(&target);
            }
            for ancestor in rel.ancestors().skip(1){folders.insert(dir.join(ancestor));}
        }
        for folder in folders.into_iter().rev(){if ordinary(dir,&folder).unwrap_or(false){let _=fs::remove_dir(folder);}}
        if dir.exists(){warn!("Preserved a changed update backup at {:?}",dir);}
    }
}

pub(crate) fn check_preserved_install_target(pkg:&bundle::BundleZip, root:&Path, id:&str, version:&semver::Version)->Result<()>{
    ensure!(fs::symlink_metadata(root)?.file_attributes()&0x400==0,"Select an ordinary installation folder");
    let mut proposed=vec![PathBuf::from("Update.exe"),PathBuf::from("current/sq.version"),
        Path::new("packages").join(format!("{}-{}-full.nupkg",id,version)),PathBuf::from("packages/.velopack_lock")];
    for entry in pkg.get_file_names()?{if let Some(rel)=owned_target(&entry)?{proposed.push(rel);}}
    for rel in proposed{
        let mut path=root.to_owned();
        for term in rel.components(){path.push(term);if let Ok(meta)=fs::symlink_metadata(&path){
            ensure!(meta.is_dir() && meta.file_attributes()&0x400==0 && path!=root.join(&rel),
                "Installation would overwrite an existing entry: {:?}. Choose another folder.",path);}}
    }
    Ok(())
}
pub(crate) fn check_repair_contents(locator:&VelopackLocator)->Result<()>{
    let root=locator.get_root_dir();
    ensure!(fs::symlink_metadata(&root)?.file_attributes()&0x400==0,"Select an ordinary installation folder");
    let (known,_,_)=inventory(locator)?;
    let mut keep:BTreeSet<_>=known.into_iter().collect();
    keep.insert(locator.get_update_path());
    let lock=locator.get_packages_dir().join(".velopack_lock");
    if ordinary(&root,&lock)? && fs::metadata(&lock)?.len()==0{keep.insert(lock);}
    for entry in walkdir::WalkDir::new(&root).follow_links(false){
        let item=entry?;let meta=fs::symlink_metadata(item.path())?;
        ensure!(meta.file_attributes()&0x400==0 && (meta.is_dir() || keep.contains(item.path())),
            "Existing user files would be overwritten during repair. Uninstall first, or use another folder.");
    }
    Ok(())
}

pub fn uninstall(locator:&VelopackLocator, delete_self:bool)->Result<()>{
    ensure!(!locator.get_is_msi_install(),"Use Windows Installer to remove this installation");
    ensure!(delete_self,"Unsupported external removal invocation");
    check_location(locator)?;
    let lock_path=locator.get_packages_dir().join(".velopack_lock");
    if lock_path.exists(){ensure!(ordinary(&locator.get_root_dir(),&lock_path)? && fs::metadata(&lock_path)?.len()==0,"Unknown lock file retained");}
    let lock=locator.try_get_exclusive_lock()?;
    let (files,dirs,_)=inventory(locator)?;
    let root=locator.get_root_dir();
    // The upstream updater makes its own directory the process working directory.
    // Windows holds that directory without delete sharing until it changes.
    std::env::set_current_dir(root.parent().context("Installation root has no parent")?)?;
    let finish=DelayedCleanup::start(&root,&locator.get_update_path())?;
    let title=locator.get_manifest_title();
    let progress=dialogs::progress::show_uninstall_progress(&title);
    shared::stop_owned_processes(&root,&locator.get_main_exe_path())?;
    // Detect exclusive external locks before unlinking any entry. Windows releases
    // the probe immediately; the authoritative package is retained until the end.
    use std::os::windows::fs::OpenOptionsExt;
    for f in &files {let _probe=fs::OpenOptions::new().access_mode(0x10080).share_mode(7)
        .custom_flags(0x00200000).open(f).with_context(||format!("A locked file is still in use: {:?}",f))?;}
    if !windows::run_hook(locator,constants::HOOK_CLI_UNINSTALL,60){warn!("Uninstall hook returned unsuccessfully");}
    for f in &files{
        if ordinary(&root,f)? {shared::retry_io(||fs::remove_file(f)).with_context(||format!("Unable to remove {:?}",f))?;}
    }
    drop(lock);
    if ordinary(&root,&lock_path)? && fs::metadata(&lock_path)?.len()==0 {fs::remove_file(&lock_path)?;}
    for d in dirs{
        if ordinary(&root,&d)? && d.is_dir(){let _=fs::remove_dir(d);}
    }
    windows::remove_owned_uninstall_shortcut(locator)?;
    windows::registry::remove_uninstall_entry(locator)?;
    finish.commit();
    progress.close();dialogs::show_uninstall_complete(&title);
    info!("Removal committed. Windows will release the updater after this process exits.");
    Ok(())
}
