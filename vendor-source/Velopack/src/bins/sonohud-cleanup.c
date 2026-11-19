/* SonoHUD contributors, 2026. MIT. Windows per-user installer maintenance. */
#define UNICODE
#define _UNICODE
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <wchar.h>
#include <stdint.h>
#include <stdio.h>

#ifdef SONOHUD_CLEANUP_MODULE
/* rundll32 unloads the module when this callback returns. Windows then closes
   the inherited delete-on-close handle to its scratch file. No filename is
   interpreted as a removal instruction. Both targets are parent-opened objects. */
__declspec(dllexport) void CALLBACK FinishUninstallW(HWND window, HINSTANCE instance, LPWSTR args, int show) {
    unsigned long long p, u, d, r, c;
    wchar_t extra;
    if (swscanf_s(args, L"%llu %llu %llu %llu %llu %lc", &p, &u, &d, &r, &c, &extra, 1) != 5) return;
    HANDLE parent=(HANDLE)(uintptr_t)p, updater=(HANDLE)(uintptr_t)u, dir=(HANDLE)(uintptr_t)d;
    HANDLE ready=(HANDLE)(uintptr_t)r, commit=(HANDLE)(uintptr_t)c;
    wchar_t dirpath[32768], exepath[32768];
    DWORD dn=GetFinalPathNameByHandleW(dir, dirpath, 32768, 0);
    DWORD en=GetFinalPathNameByHandleW(updater, exepath, 32768, 0);
    FILE_ATTRIBUTE_TAG_INFO da, ea;
    if (!dn || !en || en>=32768 || dn>=32768 ||
        !GetFileInformationByHandleEx(dir, FileAttributeTagInfo, &da, sizeof(da)) ||
        !GetFileInformationByHandleEx(updater, FileAttributeTagInfo, &ea, sizeof(ea)) ||
        (da.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) || !(da.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) ||
        (ea.FileAttributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) ||
        en != dn+11 || _wcsnicmp(dirpath, exepath, dn) || _wcsicmp(exepath+dn,L"\\Update.exe")) return;
    if (!SetEvent(ready) || WaitForSingleObject(parent, INFINITE)!=WAIT_OBJECT_0 ||
        WaitForSingleObject(commit, 0)!=WAIT_OBJECT_0) return;
    FILE_DISPOSITION_INFO dispose={TRUE};
    for (unsigned i=0;i<100;i++) {
        if (SetFileInformationByHandle(updater, FileDispositionInfo, &dispose, sizeof(dispose))) break;
        DWORD err=GetLastError();
        if (err!=ERROR_ACCESS_DENIED && err!=ERROR_SHARING_VIOLATION) return;
        Sleep(100);
    }
    CloseHandle(updater);
    /* Windows will reject this call for any directory that still has entries. */
    SetFileInformationByHandle(dir, FileDispositionInfo, &dispose, sizeof(dispose));
    CloseHandle(dir);
}
#else
#include <bcrypt.h>
static void close_valid(HANDLE h) { if(h && h!=INVALID_HANDLE_VALUE) CloseHandle(h); }

/* Called only after the Rust owner has matched the executing updater, installed
   HKCU location and Core package. Returns the event needed to authorize cleanup. */
DWORD sonohud_start_cleanup(const wchar_t* update, const wchar_t* root, const unsigned char* bytes, DWORD size, HANDLE* result) {
    SECURITY_ATTRIBUTES sa={sizeof(sa), NULL, TRUE};
    HANDLE parent=NULL, updater=INVALID_HANDLE_VALUE, dir=INVALID_HANDLE_VALUE, writer=INVALID_HANDLE_VALUE;
    HANDLE scratch=INVALID_HANDLE_VALUE, ready=NULL, commit=NULL;
    LPPROC_THREAD_ATTRIBUTE_LIST list=NULL;
    BOOL initialized=FALSE;
    PROCESS_INFORMATION pi={0};
    DWORD error=0, written=0;
    wchar_t tmp[MAX_PATH+1], dll[MAX_PATH+1], host[MAX_PATH+1], command[2*MAX_PATH+190];
    unsigned char nonce[16];
    *result=NULL;
    if(!GetTempPathW(MAX_PATH,tmp) || !GetSystemDirectoryW(host,MAX_PATH) ||
        wcscat_s(host,MAX_PATH,L"\\rundll32.exe") || BCryptGenRandom(NULL,nonce,sizeof(nonce),BCRYPT_USE_SYSTEM_PREFERRED_RNG)!=0)
        return ERROR_BAD_PATHNAME;
    wchar_t hex[33];
    for(unsigned i=0;i<16;i++) swprintf_s(hex+2*i, 33-2*i, L"%02x", nonce[i]);
    if(swprintf_s(dll,MAX_PATH,L"%lsSonoHUD-uninstall-%ls.dll",tmp,hex)<0) return ERROR_FILENAME_EXCED_RANGE;
    writer=CreateFileW(dll,GENERIC_READ|GENERIC_WRITE|DELETE,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                       NULL,CREATE_NEW,FILE_ATTRIBUTE_TEMPORARY|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(writer==INVALID_HANDLE_VALUE){error=GetLastError();goto done;}
    if(!WriteFile(writer,bytes,size,&written,NULL) || written!=size){error=ERROR_WRITE_FAULT;goto done;}
    scratch=ReOpenFile(writer,GENERIC_READ|DELETE,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                       FILE_FLAG_DELETE_ON_CLOSE|FILE_FLAG_OPEN_REPARSE_POINT);
    if(scratch==INVALID_HANDLE_VALUE){error=GetLastError();goto done;}
    CloseHandle(writer);writer=INVALID_HANDLE_VALUE;
    /* Retain a second non-write-sharing handle through the child's lifetime. */
    HANDLE guard=ReOpenFile(scratch,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_DELETE,FILE_FLAG_OPEN_REPARSE_POINT);
    if(guard==INVALID_HANDLE_VALUE){error=GetLastError();goto done;}
    if(!SetHandleInformation(scratch,HANDLE_FLAG_INHERIT,HANDLE_FLAG_INHERIT) ||
       !SetHandleInformation(guard,HANDLE_FLAG_INHERIT,HANDLE_FLAG_INHERIT)) {error=GetLastError();CloseHandle(guard);goto done;}
    parent=OpenProcess(SYNCHRONIZE,TRUE,GetCurrentProcessId());
    if(!parent){error=GetLastError();CloseHandle(guard);goto done;}
    updater=CreateFileW(update, DELETE|FILE_READ_ATTRIBUTES,7,&sa,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(updater==INVALID_HANDLE_VALUE){error=GetLastError();CloseHandle(guard);goto done;}
    dir=CreateFileW(root,DELETE|FILE_READ_ATTRIBUTES,7,&sa,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    if(dir==INVALID_HANDLE_VALUE){error=GetLastError();CloseHandle(guard);goto done;}
    ready=CreateEventW(&sa,TRUE,FALSE,NULL);
    if(!ready){error=GetLastError();CloseHandle(guard);goto done;}
    commit=CreateEventW(&sa,TRUE,FALSE,NULL);
    if(!commit){error=GetLastError();CloseHandle(guard);goto done;}

    HANDLE pass[]={parent,updater,dir,ready,commit,scratch,guard};
    SIZE_T list_size=0;
    InitializeProcThreadAttributeList(NULL,1,0,&list_size);
    list=(LPPROC_THREAD_ATTRIBUTE_LIST)HeapAlloc(GetProcessHeap(),0,list_size);
    if(!list || !InitializeProcThreadAttributeList(list,1,0,&list_size)){error=ERROR_NOT_ENOUGH_MEMORY;CloseHandle(guard);goto done;}
    initialized=TRUE;
    if(!UpdateProcThreadAttribute(list,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,pass,sizeof(pass),NULL,NULL)){
        error=GetLastError();CloseHandle(guard);goto done;}
    STARTUPINFOEXW si={0};si.StartupInfo.cb=sizeof(si);si.lpAttributeList=list;
    swprintf_s(command,sizeof(command)/sizeof(command[0]),L"\"%ls\" \"%ls\",FinishUninstall %llu %llu %llu %llu %llu",
        host,dll,(unsigned long long)(uintptr_t)parent,(unsigned long long)(uintptr_t)updater,
        (unsigned long long)(uintptr_t)dir,(unsigned long long)(uintptr_t)ready,(unsigned long long)(uintptr_t)commit);
    /* Use a system absolute path and explicit inherited handles. No shell or desktop activation. */
    BOOL ok=CreateProcessW(host,command,NULL,NULL,TRUE,EXTENDED_STARTUPINFO_PRESENT|CREATE_NO_WINDOW,
                           NULL,tmp,&si.StartupInfo,&pi);
    if(!ok) error=GetLastError();
    CloseHandle(guard);
    if(ok){
        HANDLE events[]={ready,pi.hProcess};
        if(WaitForMultipleObjects(2,events,FALSE,10000)!=WAIT_OBJECT_0) error=ERROR_SERVICE_REQUEST_TIMEOUT;
        else {*result=commit;commit=NULL;}
    }
done:
    if(initialized) DeleteProcThreadAttributeList(list);
    if(list) HeapFree(GetProcessHeap(),0,list);
    if(writer!=INVALID_HANDLE_VALUE && scratch==INVALID_HANDLE_VALUE){
        FILE_DISPOSITION_INFO remove={TRUE};SetFileInformationByHandle(writer,FileDispositionInfo,&remove,sizeof(remove));}
    close_valid(writer);close_valid(scratch);close_valid(pi.hThread);close_valid(pi.hProcess);
    close_valid(parent);close_valid(updater);close_valid(dir);close_valid(ready);close_valid(commit);
    return !error && !*result ? ERROR_INVALID_HANDLE : error;
}

void sonohud_commit_cleanup(HANDLE event) { SetEvent(event); CloseHandle(event); }
void sonohud_cancel_cleanup(HANDLE event) { CloseHandle(event); }
#endif
