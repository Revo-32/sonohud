#![allow(unused_variables)]

use std::env;

fn main() {
    #[cfg(target_os = "windows")]
    { harden_dll_loading(); build_uninstaller(); }

    let version = env!("CARGO_PKG_VERSION");
    println!("cargo:rustc-env=NGBV_VERSION={}", version);
    assert_eq!(version, "1.2.161", "check the fixed Windows VERSIONINFO before updating Velopack");
    winres::WindowsResource::new()
        .set_resource_file(concat!(env!("CARGO_MANIFEST_DIR"), "/sonohud-version.rc"))
        .compile().unwrap();
    println!("cargo:rerun-if-changed=sonohud-version.rc");
    println!("cargo:rerun-if-changed=app.manifest");
    println!("cargo:rustc-link-arg={}/resource.o",env::var("OUT_DIR").unwrap());
}

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
