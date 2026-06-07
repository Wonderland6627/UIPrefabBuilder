using System.Collections.Generic;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Core
{
    public interface IAgentAction
    {
        string ActionName { get; }
        string Description { get; }
        ActionResult Execute(ActionContext context);
    }

    public class ActionContext
    {
        public GameObject TargetObject { get; set; }
        public GameObject CanvasRoot { get; set; }
        public string WorkingPrefabPath { get; set; }
    }

    public class ActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public static ActionResult Ok(string message) => new ActionResult { Success = true, Message = message };
        public static ActionResult Fail(string message) => new ActionResult { Success = false, Message = message };
    }
}
