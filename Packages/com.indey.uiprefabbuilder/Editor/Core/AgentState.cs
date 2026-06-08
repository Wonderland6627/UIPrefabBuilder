namespace Indey.UIPrefabBuilder.Core
{
    public enum AgentState
    {
        Idle,
        Thinking,
        CallingTool,
        WaitingConfirmation,
        Completed,
        Error,

        // Legacy states kept for backward compatibility
        BuildingContext,
        WaitingForLLM,
        ExtractingCode,
        Compiling,
        Executing,
        ObservingResult
    }
}
