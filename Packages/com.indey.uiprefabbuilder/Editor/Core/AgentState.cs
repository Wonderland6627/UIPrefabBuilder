namespace Indey.UIPrefabBuilder.Core
{
    public enum AgentState
    {
        Idle,
        BuildingContext,
        WaitingForLLM,
        ExtractingCode,
        Compiling,
        Executing,
        ObservingResult,
        WaitingConfirmation,
        Error
    }
}
