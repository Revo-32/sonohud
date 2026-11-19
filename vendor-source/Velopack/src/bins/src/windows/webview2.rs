use winreg::{enums::*, RegKey};

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
