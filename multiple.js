// multiply_demo.js — WSH JScript
// Demonstrates RegisterCode: inline machine code that multiplies two numbers,
// with the result displayed in a MessageBox — no DLL, no exports, no P/Invoke.
//
// Run: cscript.exe .\multiply_demo.js

var DWX = new ActiveXObject("DynaCall.DynamicWrapper");

// ── MessageBoxW — for showing the result ──────────────────────────────────────
DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");

// ── Inline machine code ───────────────────────────────────────────────────────
//
// x86 (32-bit) — cdecl / stdcall: arguments on the stack
//   8B 44 24 04   mov eax, [esp+4]     ; first arg  → eax
//   F7 6C 24 08   imul dword [esp+8]   ; eax * second arg, low 32 bits → eax
//   C3            ret
//
// x64 — Windows fastcall: first arg in rcx, second in rdx, return in rax
//   48 89 C8      mov rax, rcx         ; first arg  → rax
//   48 F7 EA      imul rdx             ; rax * rdx, low 64 bits → rax
//   C3            ret

var code;
if (DWX.Bitness == 32) {
    code = "8B442404 F76C2408 C3";
} else {
    code = "4889C8 48F7EA C3";
}

// RegisterCode allocates executable memory, writes the bytes, and registers
// "Multiply" as a callable name — no DLL involved.
DWX.RegisterCode(code, "Multiply", "i=ll", "f=s", "r=l");

// ── Call the machine code function ────────────────────────────────────────────

var a      = 5;
var b      = 4;
var result = DWX.Call("Multiply", a, b);

DWX.Call("MessageBoxW",
    0,
    "" + a + " \u00D7 " + b + " = " + result,   // e.g. "5 × 4 = 20"
    "RegisterCode Demo — DynaCall",
    0);