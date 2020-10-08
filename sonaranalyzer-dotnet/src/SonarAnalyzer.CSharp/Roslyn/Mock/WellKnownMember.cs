
namespace Microsoft.CodeAnalysis
{
    internal class WellKnownMember
    {
        public static WellKnownMember System_Threading_Monitor__Enter, System_Threading_Monitor__Enter2, System_Threading_Monitor__Exit,
            Microsoft_VisualBasic_CompilerServices_ObjectFlowControl_ForLoopControl__ForLoopInitObj,
            Microsoft_VisualBasic_CompilerServices_ObjectFlowControl_ForLoopControl__ForNextCheckObj,
            System_Runtime_CompilerServices_SwitchExpressionException__ctor,
            System_InvalidOperationException__ctor,
            System_IAsyncDisposable__DisposeAsync
            ;
    }

    internal class WellKnownMembers
    {
        public int ParametersCount;

        public static WellKnownMembers GetDescriptor(WellKnownMember member) => null; // Cheating by returning same type
    }
}
