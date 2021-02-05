using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public delegate Task OnExceptionHandler(ExceptionContext ctx);
}