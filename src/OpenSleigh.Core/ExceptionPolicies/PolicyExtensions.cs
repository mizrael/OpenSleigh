using System;
using System.Threading.Tasks;

namespace OpenSleigh.Core.ExceptionPolicies
{
    public static class PolicyExtensions
    {
        public static Task WrapAsync(this IPolicy policy, Func<Task> action)
        {
            Func<Task<bool>> wrappedAction = async () =>
            {
                await action();
                return true;
            };
            return policy.WrapAsync(wrappedAction);
        }
    }
}