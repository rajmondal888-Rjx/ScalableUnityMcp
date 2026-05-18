using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    /// <summary>
    /// Wraps a Func&lt;JObject,JObject&gt; as an IToolHandler.
    /// Used by multi-tool handler classes so each tool name maps to a separate delegate.
    /// </summary>
    public class DelegateToolHandler : ToolHandlerBase
    {
        private readonly Func<JObject, JObject> _fn;
        public override string Name { get; }

        public DelegateToolHandler(string name, Func<JObject, JObject> fn)
        {
            Name = name;
            _fn  = fn;
        }

        public override JObject Execute(JObject parameters) => _fn(parameters);
    }
}
