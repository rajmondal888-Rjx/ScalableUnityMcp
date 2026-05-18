using ScalableMCP.Editor;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler for available tests from the Unity Test Runner.
    /// URI: unity://tests/{testMode}
    /// </summary>
    public class TestsResourceHandler : ResourceHandlerBase
    {
        public override string Name     => "get_tests";
        public override string Uri      => "unity://tests/{testMode}";
        public override bool   IsAsync  => true;

        private readonly TestRunnerService _testRunnerService;

        public TestsResourceHandler(TestRunnerService testRunnerService)
        {
            _testRunnerService = testRunnerService;
        }

        public override async void FetchAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            string testModeFilter         = parameters?["testMode"]?.ToObject<string>();
            List<ITestAdaptor> allTests   = await _testRunnerService.GetAllTestsAsync(testModeFilter);

            var results = new JArray();
            foreach (ITestAdaptor test in allTests)
            {
                results.Add(new JObject
                {
                    ["name"]      = test.Name,
                    ["fullName"]  = test.FullName,
                    ["testMode"]  = test.TestMode.ToString(),
                    ["runState"]  = test.RunState.ToString()
                });
            }

            tcs.SetResult(new JObject
            {
                ["success"] = true,
                ["message"] = $"Retrieved {allTests.Count} tests",
                ["tests"]   = results
            });
        }
    }
}
