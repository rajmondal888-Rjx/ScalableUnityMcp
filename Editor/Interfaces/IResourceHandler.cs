using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    public interface IResourceHandler
    {
        string Name { get; }
        string Uri { get; }
        bool IsAsync { get; }
        JObject Fetch(JObject parameters);
        void FetchAsync(JObject parameters, TaskCompletionSource<JObject> tcs);
    }

    public abstract class ResourceHandlerBase : IResourceHandler
    {
        public abstract string Name { get; }
        public abstract string Uri { get; }
        public virtual bool IsAsync => false;

        public virtual JObject Fetch(JObject parameters)
            => JsonResponseFactory.Error("Fetch not implemented", "implementation_error");

        public virtual void FetchAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
            => tcs.TrySetException(new System.NotImplementedException("FetchAsync not implemented"));
    }
}
