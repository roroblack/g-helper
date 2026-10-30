using System.Buffers.Binary;
// Review-only reusable getter core. No loader, P/Invoke, service, device or live entry point.
public static class SharedPowerGetter {
 [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
 public unsafe delegate int Get(nint handle,byte* buffer);
 public sealed record Sample(double? Watts,string Reason){public string Label=>"CPU+Memory";}
 static uint U(byte[] b,int o)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
 public static Sample Decode(byte[] info,byte[] status){
  if(info.Length!=3240||status.Length!=9432)return new(null,"local policy: exact buffer size mismatch");
  if(U(info,0)!=0x30ca8||U(status,0)!=0x124d8)return new(null,"local policy: version mismatch");
  double raw=0;bool positiveSource=false;
  for(int i=0;i<32;i++){
   if((U(info,16)&(1u<<i))==0||U(info,52+60*i)==9)continue;
   int sign=info[56+60*i] switch{0xa4=>1,0xa5 or 0xa6 or 0xa7 or 0xe8=>-1,_=>0};
   if(sign==0)continue;
   if((U(status,4)&(1u<<i))==0)return new(null,"local policy: missing contributing returned-mask bit");
   uint value=U(status,80+216*i);
   if(value is uint.MaxValue or 0x80000000u)return new(null,"local policy: candidate invalid numeric marker; official sentinel unknown");
   positiveSource|=sign==1;raw+=sign*(double)value;
  }
  if(!positiveSource||raw<=0)return new(null,"local policy: no positive shared aggregate (reference uses >0)");
  return new(raw/1000.0,"HWMonitor-compatible shared formula; single sample, not 8-sample average");
 }
 public sealed record Receipt(Sample Value,int InfoStatus,int? StatusStatus,byte[] InfoBefore,byte[] InfoAfter,byte[]? StatusBefore,byte[]? StatusAfter);
 // Only externally supplied, separately approved getter pointers may be adapted to Get.
 // This review ships no resolver or live invocation path. Arrays are exact-size and pinned.
 public static unsafe Receipt Acquire(nint handle,Get getInfo,Get getStatus){
  byte[] info=InfoInput(),before=(byte[])info.Clone();int a;
  fixed(byte* p=info)a=getInfo(handle,p);
  if(a!=0)return new(new(null,"metadata status "+a),a,null,before,info,null,null);
  byte[] status=StatusInput(U(info,16)),sb=(byte[])status.Clone();int b;
  fixed(byte* p=status)b=getStatus(handle,p);
  return new(b==0?Decode(info,status):new(null,"status getter status "+b),a,b,before,info,sb,status);
 }
 public static byte[] InfoInput(){var b=new byte[3240];BinaryPrimitives.WriteUInt32LittleEndian(b,0x00030ca8);return b;}
 public static byte[] StatusInput(uint primaryMask){var b=new byte[9432];BinaryPrimitives.WriteUInt32LittleEndian(b,0x000124d8);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4),primaryMask);return b;}
}
