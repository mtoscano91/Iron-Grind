namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>Creates the pooled <see cref="INavAgent"/> instances (Unity adapter: Story 009).</summary>
    public interface INavAgentFactory
    {
        /// <summary>Creates one pooled agent under the named root. Called only at Initialize.</summary>
        /// <param name="poolRootName">Name of the root the agent is created under.</param>
        /// <returns>The new agent.</returns>
        INavAgent Create(string poolRootName);
    }
}
