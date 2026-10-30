using System.Runtime.InteropServices;
public static class ProbePolicy {
 public const string StubPath=@"C:\Windows\System32\nvapia64.dll";
 public const string StubHash="01ea347d7917cb07781d3112e0840fcbce8474304bc30cf0f5c2b1a872a3171f";
 public const string ImplPath=@"C:\Windows\System32\DriverStore\FileRepository\nv_asus_woa.inf_arm64_a6f21714139dd573\nvapia64_impl.dll";
 public const string ImplHash="de593b295559b3d0c0ed671d80c90b6b6a01d11190cb635e866f1aca39789f70";
 public static bool AllowedPointer(uint id,string path,long rva)=>string.Equals(path,StubPath,StringComparison.OrdinalIgnoreCase)?id==0x0150e828&&rva==0xd9c0:string.Equals(path,ImplPath,StringComparison.OrdinalIgnoreCase)&&(id switch{0xe5ac921f=>rva==0x1a4a80,0xc12eb19e=>rva==0x274f50,0xf40238ef=>rva==0x275a30,_=>false});
 public static bool Allowed(uint id)=>id is 0x0150e828 or 0xe5ac921f or 0xc12eb19e or 0xf40238ef;
 public static bool ValidateIdentity(string path,string hash,ushort machine,Architecture arch)=>arch==Architecture.Arm64 && machine==0xaa64 && ((string.Equals(path,StubPath,StringComparison.OrdinalIgnoreCase)&&string.Equals(hash,StubHash,StringComparison.OrdinalIgnoreCase))||(string.Equals(path,ImplPath,StringComparison.OrdinalIgnoreCase)&&string.Equals(hash,ImplHash,StringComparison.OrdinalIgnoreCase)));
}
