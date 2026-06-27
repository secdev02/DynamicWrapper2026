var DWX = new ActiveXObject("DynaCall.DynamicWrapper");
DWX.Register("user32.dll", "MessageBoxW", "i=hwwu", "f=s", "r=l");
DWX.Call("MessageBoxW", 0, "Hello from .NET DynaCall!", "Demo", 0);