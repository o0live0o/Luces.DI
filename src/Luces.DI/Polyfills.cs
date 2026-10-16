// The netstandard2.0 BCL lacks IsExternalInit; without it the compiler cannot generate
// initialization logic for records and init-only members. The type is looked up by the compiler
// under the fully qualified name System.Runtime.CompilerServices.IsExternalInit, so the namespace cannot change.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
