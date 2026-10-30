using System.Runtime.InteropServices;
// No load/device/driver/service/config path. Caller must supply an independently validated resolver.
public sealed class ReadOnlySharedPowerAdapter {
 readonly SharedPowerGetter.Get info,status;
 public ReadOnlySharedPowerAdapter(Func<uint,nint> resolver){
  nint a=resolver(0xc12eb19e),b=resolver(0xf40238ef);
  if(a==0||b==0)throw new NotSupportedException("shared getter unavailable");
  info=Marshal.GetDelegateForFunctionPointer<SharedPowerGetter.Get>(a);
  status=Marshal.GetDelegateForFunctionPointer<SharedPowerGetter.Get>(b);
 }
 public SharedPowerGetter.Receipt Read(nint physicalGpuHandle)=>SharedPowerGetter.Acquire(physicalGpuHandle,info,status);
}
