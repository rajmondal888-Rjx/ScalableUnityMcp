using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    public interface IToolHandler
    {
        string Name { get; }
        bool IsAsync { get; }
        JObject Execute(JObject parameters);
        void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs);
    }

    public abstract class ToolHandlerBase : IToolHandler
    {
        public abstract string Name { get; }
        public virtual string Description => $"Custom Unity tool: {Name}";
        public virtual string Category => "Custom";

        // Return a flat JObject like { "paramName": { "type": "string", "description": "...", "required": true } }
        // Return null if the tool takes no parameters.
        public virtual JObject ParamsSchema => null;

        public virtual bool IsAsync => false;

        public virtual JObject Execute(JObject parameters)
            => JsonResponseFactory.Error("Execute not implemented", "implementation_error");

        public virtual void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
            => tcs.TrySetException(new System.NotImplementedException("ExecuteAsync not implemented"));
    }
}
