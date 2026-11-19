"""Prepare the editable Velopack sources shared by the Core source archive and installer build."""
import argparse
import hashlib
from pathlib import Path
import shutil
import urllib.request
import zipfile

VERSION = '1.2.161'
COMMIT = '92d6a1c91716729d449034df5c50307dcce39493'
ARCHIVE_SHA = '3e35756fddcc9f966792411268e3c085a44b482a02942005e4a3f6827c4a50a7'
GIT_URL = f'https://codeload.github.com/velopack/velopack/zip/{COMMIT}'

# Microsoft's documented registry check for an installed Evergreen runtime.
# SonoHUD only requests .NET Desktop. Keeping this probe also preserves the upstream
# helper's ability to identify an Evergreen prerequisite without a bundled SDK loader.
RUNTIME_PROBE = r'''use winreg::{enums::*, RegKey};

pub fn get_webview_version() -> Option<String> {
    const CLIENT: &str = r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    for root in [HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER] {
        let parent = RegKey::predef(root);
        if let Ok(key) = parent.open_subkey_with_flags(CLIENT, KEY_READ | KEY_WOW64_32KEY) {
            if let Ok(version) = key.get_value::<String, _>("pv") {
                if !version.is_empty() && version != "0.0.0.0" {
                    return Some(version);
                }
            }
        }
    }
    None
}
'''

VERSION_RESOURCE = '''#pragma code_page(65001)
1 VERSIONINFO
FILETYPE 0x1
FILEFLAGSMASK 0x3f
FILEVERSION 1, 2, 161, 0
PRODUCTVERSION 1, 2, 161, 0
FILEFLAGS 0x0
FILESUBTYPE 0x0
FILEOS 0x40004
{
BLOCK "StringFileInfo"
{
BLOCK "000004b0"
{
VALUE "FileDescription", "Velopack 1.2.161"
VALUE "CompanyName", "Velopack"
VALUE "LegalCopyright", "Caelan Sayler (c) 2023, Velopack Ltd. (c) 2024"
VALUE "FileVersion", "1.2.161"
VALUE "ProductVersion", "1.2.161"
VALUE "ProductName", "Velopack"
}
}
BLOCK "VarFileInfo" {
VALUE "Translation", 0x0, 0x04b0
}
}
1 24 "app.manifest"
'''

def source_tree(cache, dest):
    cache.mkdir(parents=True, exist_ok=True)
    path = cache/'velopack-upstream.zip'
    if not path.exists():
        with urllib.request.urlopen(GIT_URL,timeout=120) as source, path.open('xb') as output:
            shutil.copyfileobj(source,output)
    if hashlib.sha256(path.read_bytes()).hexdigest()!=ARCHIVE_SHA:
        raise ValueError('Velopack source fingerprint mismatch')
    with zipfile.ZipFile(path) as archive:
        prefix = 'velopack-'+COMMIT+'/'
        for item in archive.infolist():
            if item.is_dir() or not item.filename.startswith(prefix):
                continue
            rel=item.filename[len(prefix):]
            parts=Path(rel).parts
            if '..' in parts or ':' in rel or '\\' in rel: raise ValueError(rel)
            folders=('src/bins/','src/lib-rust/','src/l18n/','src/lib-csharp/','locales/')
            names={'Cargo.lock','Cargo.toml','LICENSE','README.md','version.json','global.json','src/Directory.Build.props','src/Directory.Build.targets','src/README_NUGET.md','artwork/Velopack_200.png'}
            if rel not in names and not rel.startswith(folders): continue
            target=dest/rel
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(archive.read(item))
    def edit(relative,before,after):
        target=dest/relative
        s=target.read_text(encoding='utf-8-sig')
        if before not in s: raise ValueError(f'unexpected upstream input: {relative}')
        target.write_text(s.replace(before,after,1),encoding='utf-8',newline='\n')
    # Drop foreign platform FFI & unrelated workspace members; none are run by the Windows helpers.
    top=(dest/'Cargo.toml').read_text(encoding='utf-8')
    start=top.index('members = [');end=top.index(']',start)+1
    top=top[:start]+'members = ["src/bins", "src/l18n", "src/lib-rust"]'+top[end:]
    top=top.replace('version = "0.0.0-local"',f'version = "{VERSION}"',1)
    (dest/'Cargo.toml').write_text(top,encoding='utf-8',newline='\n')
    edit('src/bins/Cargo.toml','webview2-com-sys.workspace = true','')
    edit('src/bins/Cargo.toml','    "Win32_System_Com_StructuredStorage",','    "Win32_System_Com_StructuredStorage",\n    "Win32_System_Variant",')
    (dest/'src/bins/src/windows/webview2.rs').write_text(RUNTIME_PROBE,encoding='utf-8',newline='\n')
    # Compile the small temporary Windows cleanup module and the client that
    # passes its four scoped kernel capabilities. No additional Cargo dependency.
    scripts=Path(__file__).resolve().parent
    shutil.copyfile(scripts/'velopack-uninstall.rs',dest/'src/bins/src/commands/uninstall.rs')
    shutil.copyfile(scripts/'velopack-cleanup.c',dest/'src/bins/sonohud-cleanup.c')
    legacy=scripts/'velopack-legacy-owned.tsv'
    if hashlib.sha256(legacy.read_bytes()).hexdigest()!='ad1fa1f33430af9ba2b4eed281a16078dbb7ff32751afa69a713e4a33aaa964b':
        raise ValueError('Unexpected SonoHUD.Core 0.7.2 ownership manifest')
    shutil.copyfile(legacy,dest/'src/bins/sonohud-legacy-owned.tsv')
    edit('src/bins/Cargo.toml','sha1_smol.workspace = true','sha1_smol.workspace = true\nsha2.workspace = true')
    edit('src/bins/src/windows/mod.rs','mod self_delete;','')
    edit('src/bins/src/windows/mod.rs','pub use self_delete::*;','')
    (dest/'src/bins/src/windows/self_delete.rs').unlink()
    edit('src/bins/build.rs','    delay_load();','    { harden_dll_loading(); build_uninstaller(); }')
    # GNU linkers omit an unreferenced resource-only static archive. Include the
    # resource object so every executable retains its asInvoker manifest and version.
    # winres stores VERSIONINFO fields in a randomly seeded HashMap. The fixed
    # resource contains the same upstream fields, values, translation and manifest.
    (dest/'src/bins/sonohud-version.rc').write_text(VERSION_RESOURCE, encoding='utf-8', newline='\n')
    build=dest/'src/bins/build.rs'
    original=build.read_text(encoding='utf8')
    main=original[:original.index('    let ver = semver::')]
    main+=r'''    println!("cargo:rustc-env=NGBV_VERSION={}", version);
    assert_eq!(version, "1.2.161", "check the fixed Windows VERSIONINFO before updating Velopack");
    winres::WindowsResource::new()
        .set_resource_file(concat!(env!("CARGO_MANIFEST_DIR"), "/sonohud-version.rc"))
        .compile().unwrap();
    println!("cargo:rerun-if-changed=sonohud-version.rc");
    println!("cargo:rerun-if-changed=app.manifest");
    println!("cargo:rustc-link-arg={}/resource.o",env::var("OUT_DIR").unwrap());
}
'''
    build.write_text(main, encoding='utf8', newline='\n')
    with (dest/'src/bins/build.rs').open('a',encoding='utf-8',newline='\n') as output:
        output.write(r'''
#[cfg(target_os = "windows")]
fn harden_dll_loading() {
    assert_eq!(env::var("TARGET").unwrap(), "x86_64-pc-windows-gnullvm");
    println!("cargo:rustc-link-arg=-Wl,--dependent-load-flag=0x800");
    for dll in ["gdi32","advapi32","shell32","ole32","psapi","propsys","secur32","crypt32","ws2_32","oleaut32","bcrypt","comctl32"] {
        println!("cargo:rustc-link-arg=-Wl,--delayload={dll}.dll");
    }
    println!("cargo:rustc-link-arg=-ldelayimp");
}
#[cfg(target_os = "windows")]
fn build_uninstaller() {
    use std::{path::PathBuf,process::Command};
    let out=PathBuf::from(std::env::var_os("OUT_DIR").unwrap());
    let source=PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("sonohud-cleanup.c");
    let run=|name:&str,args:&[&str]|{
        assert!(Command::new(name).current_dir(&out).args(args).status().expect(name).success(), "{}",name);
    };
    let input=source.to_str().unwrap();
    run("x86_64-w64-mingw32-clang", &["-O2","-shared","-DSONOHUD_CLEANUP_MODULE",input,
        "-Wl,--dependent-load-flag=0x800,--no-insert-timestamp","-o","SonoHudCleanup.dll"]);
    run("x86_64-w64-mingw32-clang", &["-O2","-c",input,"-o","launcher.o"]);
    run("llvm-ar", &["rcsD","libsonohud_uninstall.a","launcher.o"]);
    println!("cargo:rustc-link-search=native={}",out.display());
    println!("cargo:rustc-link-lib=static=sonohud_uninstall");
    println!("cargo:rustc-link-lib=bcrypt");
    println!("cargo:rerun-if-changed=sonohud-cleanup.c");
}
''')
    edit('src/bins/src/shared/util_windows.rs','fn _force_stop_package',r'''pub fn stop_owned_processes(root:&Path,main:&Path)->Result<()> {
    for (pid,image,handle) in unsafe { get_processes_running_in_directory(root)? } {
        if pid!=std::process::id() && same_file::is_same_file(&image,main).unwrap_or(false) {
            process::kill_process(&handle)?;
        }
    }
    Ok(())
}

fn _force_stop_package''')
    edit('src/bins/src/windows/util.rs','    let _ = shared::force_stop_package(&root_dir);','    let _ = shared::stop_owned_processes(&root_dir,&locator.get_main_exe_path());')
    edit('src/bins/src/windows/shortcuts.rs','#[inline]\nunsafe fn create_instance',r'''pub fn remove_owned_uninstall_shortcut(locator:&VelopackLocator)->Result<()> {
    let root=locator.get_root_dir();
    let main=locator.get_main_exe_path();
    let update=locator.get_update_path();
    let stub=root.join("SonoHUD.exe");
    let expected=get_path_for_shortcut_location(&locator.get_manifest_id(),&locator.get_manifest_title(),
        &locator.get_manifest_authors(),ShortcutLocationFlags::START_MENU_ROOT)?;
    unsafe { unsafe_run_delegate_in_com_context(move || {
        for (_,entry) in unsafe_get_shortcuts_for_root_dir(&root){
            let link=entry.get_link_path();
            if link!=expected{continue;}
            if let Ok(target)=entry.get_target_path(){
                if target==main || target==update || target==stub{unsafe_delete_lnk_file(&link,false)?;}
            }
        }
        Ok(())
    }) }
}

#[inline]
unsafe fn create_instance''')
    apply='src/bins/src/commands/apply_windows_impl.rs'
    edit(apply,'    let mut bundle = load_bundle_from_file(package)', '    let ownership=super::UpdateOwnership::load(old_locator)?;\n    let mut bundle = load_bundle_from_file(package)')
    edit(apply,'        let _ = shared::force_stop_package(&root_path);','        shared::stop_owned_processes(&root_path,&old_locator.get_main_exe_path())?;')
    edit(apply,'        // fifth, we try to replace the current dir with temp_path_new','        ownership.verify(&temp_path_old)?;\n        // fifth, we try to replace the current dir with temp_path_new')
    edit(apply,'        remove_temp_dir_timed(&temp_path_old);','        ownership.remove_verified_backup(&temp_path_old);')
    edit(apply,'    remove_temp_dir_timed(&temp_path_old);\n    action?;',r'''    if action.is_err() && temp_path_old.exists() {
        if !current_dir.exists() {
            shared::retry_io(||fs::rename(&temp_path_old,&current_dir)).context("Restore the preserved previous installation before retrying")?;
        } else {
            warn!("Previous installation preserved at {:?}",temp_path_old);
        }
    }
    action?;''')
    # An unregistered directory may hold user files left behind by a previous
    # uninstall. Install around non-overlapping paths and reject collisions.
    edit('src/bins/src/commands/install.rs','    if !shared::is_dir_empty(&root_path) {',r'''    let existing=auto_locate_app_manifest(LocationContext::FromSpecifiedRootDir(root_path.clone(), None)).ok();
    let preserved=!shared::is_dir_empty(&root_path) && existing.is_none();
    if preserved { super::check_preserved_install_target(pkg,&root_path,&app.id,&app.version)?; }
    if !shared::is_dir_empty(&root_path) && !preserved {
        let old=existing.as_ref().unwrap();
        anyhow::ensure!(old.get_manifest_id()==app.id,"Another application already owns this directory");
        super::check_repair_contents(old)?;
''')
    edit('src/bins/src/commands/install.rs','        shared::force_stop_package(&root_path).map_err','        shared::stop_owned_processes(&root_path,&existing.as_ref().unwrap().get_main_exe_path()).map_err')
    edit('src/bins/src/commands/install.rs','    remove_dir_all::ensure_empty_dir(&root_path)?;','    if !preserved { remove_dir_all::ensure_empty_dir(&root_path)?; }')
    # .NET product assembly restores the pinned, signed upstream package. Its editable
    # sources can be built independently with -p:SignAssembly=false without a private key.
    lock=Path(__file__).resolve().parent/'velopack-windows.lock'
    if hashlib.sha256(lock.read_bytes()).hexdigest()!='1b5a0938452c6ba6f0b9bd1e44bbbdfd41775ad83aa5a7ecad48670057feeeb9':
        raise ValueError('Unexpected Windows dependency lock')
    shutil.copyfile(lock,dest/'Cargo.lock')
    return dest

if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--cache',required=True,type=Path)
    ap.add_argument('--out',required=True,type=Path)
    args=ap.parse_args()
    if args.out.exists() and any(args.out.iterdir()):raise SystemExit('출력이 비어 있지 않습니다.')
    source_tree(args.cache,args.out)
