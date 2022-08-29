using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public interface IPolicy
    {
        Task<TRes> WrapAsync<TRes>(Func<Task<TRes>> action);
    }
}