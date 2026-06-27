# DynaCall

A managed .NET 4.8 equivalent of the classic **DynamicWrapperX** COM object. Call any exported function from any native DLL at runtime — no static `[DllImport]` declarations, no C++, no registration of unmanaged code.

<img width="324" height="234" alt="image" src="https://github.com/user-attachments/assets/810e00ec-1aba-4b30-bb21-d73c32140089" />


---

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48) (for building)
- .NET Framework 4.8 (for running — pre-installed on Windows 10 1903+)
- Windows x64 or x86
- Administrator rights for COM registration

---

## Build

```
dotnet build -c Release -r win-x64
```

For 32-bit:

```
dotnet build -c Release -r win-x86
```

Output: `bin\Release\net48\win-x64\DynaCall.dll`

---

## COM Registration

Run once from an **elevated** prompt. Use `Framework64` for x64 builds, `Framework` for x86.

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe /tlb /codebase DynaCall.dll
```

To unregister:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe /unregister DynaCall.dll
```

> The `/codebase` warning about unsigned assemblies is harmless for personal use.

---

## Quick Start — WSH JScript

```js
var DWX = new ActiveXObject("DynaCall.DynamicWrapper");

// Register a function from a DLL
DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");

// Call it
DWX.Call("MessageBoxW", 0, "Hello from DynaCall!", "Demo", 0);
```

Run with:

```
cscript.exe .\script.js
```

---

## Quick Start — Managed .NET

```csharp
using DynaCall;

using var w = new DynamicWrapper();

w.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");
w.Call("MessageBoxW", IntPtr.Zero, "Hello!", "Demo", (uint)0);
```

Add a project reference or drop `DynaCall.dll` next to your binary — no COM registration needed for managed callers.

---

## Signature Format

Every `Register` call describes the function with three strings:

| Parameter | Example | Meaning |
|---|---|---|
| `inputParams` | `"i=hwwu"` | One char per argument |
| `callingConvention` | `"f=s"` | `s` = stdcall, `c` = cdecl |
| `returnType` | `"r=l"` | One char for the return value |

All three are optional — defaults are `""`, `"f=s"`, `"r=l"`.

### Type Characters

| Char | .NET type | Native type |
|------|-----------|-------------|
| `m`  | `long`    | INT64, LONGLONG |
| `q`  | `ulong`   | UINT64, ULONGLONG |
| `l`  | `int`     | LONG, INT, BOOL |
| `u`  | `uint`    | ULONG, UINT, DWORD |
| `h`  | `IntPtr`  | HANDLE (size follows process bitness) |
| `p`  | `IntPtr`  | void\* |
| `n`  | `short`   | SHORT |
| `t`  | `ushort`  | USHORT, WORD, WCHAR |
| `c`  | `sbyte`   | CHAR (signed 8-bit) |
| `b`  | `byte`    | BYTE, UCHAR |
| `f`  | `float`   | FLOAT |
| `d`  | `double`  | DOUBLE |
| `w`  | `string`  | LPWSTR (Unicode) |
| `s`  | `string`  | LPSTR (ANSI) |
| `z`  | `string`  | LPSTR (OEM — treated as ANSI) |
| `v`  | `IntPtr`  | VARIANT\* — also used for void return (`r=v`) |

Uppercase letters denote **output / byref** parameters — e.g. `L` = `ref int`, `W` = `StringBuilder` for a Unicode output buffer.

---

## Methods

### Registration

```js
DWX.Register(dllName, functionName, [inputParams], [callingConvention], [returnType], [flags])
DWX.RegisterAddr(address, functionName, [inputParams], [callingConvention], [returnType], [flags])
DWX.RegisterCode(hexCode, [functionName], [inputParams], [callingConvention], [returnType])
DWX.RegisterCallback(callback, [inputParams], [returnType], [callingConvention])
```

Pass `"l"` in `flags` to capture the Win32 last-error code — retrieve it with `LastError()`.

### Calling

```js
DWX.Call(functionName, [arg0], [arg1], ...)   // up to 12 arguments
```

### Memory

```js
ptr  = DWX.MemAlloc(bytes, [zeroMem])         // allocate; pass 1 to zero-fill
       DWX.MemFree(ptr)                        // free
       DWX.MemZero(address, bytes)
after = DWX.MemCopy(src, dest, bytes)
hex   = DWX.MemRead(address, bytes, [bytesPerGroup], [groupsPerLine])
after = DWX.MemWrite(hexStr, destAddr, [bytes])
```

### Numbers

```js
value = DWX.NumGet(address, [offset], [type])   // default type "l"
after = DWX.NumPut(value, address, [offset], [type])
```

### Strings

```js
str   = DWX.StrGet(address, [offset], [type])   // default type "w" (Unicode)
after = DWX.StrPut(str, address, [offset], [type])
```

Pass `IntPtr.Zero` / `0` as the address to `StrPut` to query the required buffer size.

### Utilities

```js
DWX.Bitness          // 32 or 64
DWX.LastError([1])   // error code; pass 1 for a text description
DWX.Version([field]) // assembly version
DWX.Space(count)     // string of spaces; pass "" as second arg for null chars
```

---

## Examples

### MessageBox

```js
var DWX = new ActiveXObject("DynaCall.DynamicWrapper");
DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");
DWX.Call("MessageBoxW", 0, "Hello!", "Title", 0);
```

### Inline machine code — multiply

```js
var DWX  = new ActiveXObject("DynaCall.DynamicWrapper");
var code = DWX.Bitness == 32
    ? "8B442404 F76C2408 C3"       // x86: mov eax,[esp+4]; imul [esp+8]; ret
    : "4889C8 48F7EA C3";          // x64: mov rax,rcx; imul rdx; ret

DWX.RegisterCode(code, "Multiply", "i=ll", "f=s", "r=l");
DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");

var result = DWX.Call("Multiply", 6, 7);
DWX.Call("MessageBoxW", 0, "6 x 7 = " + result, "Result", 0);
```

### Native callback — EnumWindows

```js
var DWX = new ActiveXObject("DynaCall.DynamicWrapper");
DWX.Register("user32.dll", "EnumWindows",    "i=ph");
DWX.Register("user32.dll", "GetWindowTextW", "i=hpl");

var buf  = DWX.MemAlloc(512, 1);
var list = "";

var cbPtr = DWX.RegisterCallback(function(hwnd, lp) {
    DWX.Call("GetWindowTextW", hwnd, buf, 256);
    var title = DWX.StrGet(buf, "w");
    if (title.length > 0) list += hwnd + "\t" + title + "\n";
    return 1;   // return 0 to stop enumeration
}, "i=hh", "r=l");

DWX.Call("EnumWindows", cbPtr, 0);
DWX.MemFree(buf);

WScript.Echo(list);
```

---

## Notes

- **Architecture must match end-to-end** — a `win-x64` build requires 64-bit `cscript.exe` (`System32\cscript.exe`) and `Framework64` RegAsm. A `win-x86` build requires `SysWOW64\cscript.exe` and `Framework` RegAsm.
- **COM registration is required for WSH** — managed .NET callers reference the DLL directly and do not need RegAsm.
- **`Call` supports up to 12 arguments** — sufficient for any Win32 API function.
- **Callback delegates must stay alive** — `RegisterCallback` stores the delegate internally; it remains valid until `Dispose` is called (or the `DynamicWrapper` object is released in script).
- **`RegisterCode` allocates executable memory** — it is freed automatically when the object is disposed. Some security tools may flag `PAGE_EXECUTE_READWRITE` allocations.
