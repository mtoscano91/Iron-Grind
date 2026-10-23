namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Shared holder for the loaded <see cref="ZoneDefinition"/>: the scene loader fills it, the bounds validator
    /// and the NavMesh step read it. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// var slot = new ZoneDefinitionSlot();
    /// slot.Set(definition);
    /// ZoneDefinition loaded = slot.Current;
    /// </code>
    /// </example>
    public sealed class ZoneDefinitionSlot
    {
        /// <summary>The loaded definition, or null.</summary>
        public ZoneDefinition Current { get; private set; }

        /// <summary>Stores the loaded definition.</summary>
        /// <param name="definition">The definition (null clears).</param>
        public void Set(ZoneDefinition definition)
        {
            Current = definition;
        }

        /// <summary>Empties the slot.</summary>
        public void Clear()
        {
            Current = null;
        }
    }
}
