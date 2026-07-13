// ============================================================================
// PWDarkShim.cpp — native .mrr shim for PW Explorer (compile as C++/CLI, x86)
//
// Project settings:
//   - Configuration Type: DLL, output renamed/copied to PWDarkShim.mrr
//   - Platform: Win32 (x86) — pwc.exe is 32-bit
//   - C++/CLI: /clr (netfx), reference PWDarkMode.dll (or resolve at runtime)
//   - Deploy both PWDarkShim.mrr and PWDarkMode.dll to PW Explorer's bin
//
// !! VERIFY THE EXPORT NAMES !!
// The exact functions PW Explorer calls on a custom .mrr module (and the menu
// registration API — aaApi_* GUI functions) should be taken verbatim from the
// custom module sample in your ProjectWise SDK (the same sample the Flaherty
// pattern comes from). The two exports below use placeholder names — rename
// them to match the SDK sample's .def/exports before building. The managed
// forwarding is the part this file is actually demonstrating.
// ============================================================================

#include <windows.h>

using namespace System;
using namespace System::Reflection;
using namespace System::IO;

// Forward a call into PWDarkMode.dll::PWDarkMode.ModuleEntry.<method>()
static int CallManaged(const wchar_t* method)
{
    try
    {
        // Load from the same directory as this shim so we don't depend on
        // pwc.exe's working directory.
        String^ dir = Path::GetDirectoryName(Assembly::GetExecutingAssembly()->Location);
        String^ path = Path::Combine(dir, "PWDarkMode.dll");
        Assembly^ asm_ = Assembly::LoadFrom(path);
        Type^ entry = asm_->GetType("PWDarkMode.ModuleEntry");
        Object^ result = entry->GetMethod(gcnew String(method))->Invoke(nullptr, nullptr);
        return safe_cast<int>(result);
    }
    catch (Exception^)
    {
        return 1; // never let a managed exception escape into pwc.exe
    }
}

// ---------------------------------------------------------------------------
// PW custom-module exports — RENAME to match your SDK sample.
// Called by PW Explorer on the UI thread at module load/unload.
// This is also where you register the menu command via the aaApi_* GUI
// functions from the SDK sample, wiring the command handler to OnDarkToggle.
// ---------------------------------------------------------------------------

extern "C" __declspec(dllexport) int WINAPI PwModule_Initialize(void)
{
    // TODO: register menu items here per SDK sample.
    // Option A (simple): one "Cycle Theme" item -> OnThemeCycle.
    // Option B (nicer): call ModuleEntry.GetThemeList() (newline-separated
    // names), build a "Themes" submenu with one item per name, and have each
    // handler call ApplyTheme with its name.
    return CallManaged(L"Initialize");
}

extern "C" __declspec(dllexport) void WINAPI PwModule_Uninitialize(void)
{
    CallManaged(L"Shutdown");
}

// Simple single menu command: step to the next theme.
extern "C" __declspec(dllexport) void WINAPI OnThemeCycle(void)
{
    CallManaged(L"CycleTheme");
}

// Submenu handler: apply a specific named theme.
extern "C" __declspec(dllexport) void WINAPI ApplyTheme(const wchar_t* name)
{
    try
    {
        String^ dir = Path::GetDirectoryName(Assembly::GetExecutingAssembly()->Location);
        Assembly^ asm_ = Assembly::LoadFrom(Path::Combine(dir, "PWDarkMode.dll"));
        Type^ entry = asm_->GetType("PWDarkMode.ModuleEntry");
        entry->GetMethod("ApplyThemeUtf16")->Invoke(nullptr,
            gcnew array<Object^> { IntPtr((void*)name) });
    }
    catch (Exception^) { }
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
