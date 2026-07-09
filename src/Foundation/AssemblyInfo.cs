using System.Runtime.CompilerServices;

// Grants the EditMode test assembly access to internal test/authoring seams
// (ItemDefinition.SetForTesting, EquipmentData.CreateForTesting, etc.) now that
// src/Foundation compiles into its own assembly (IronGrind.Foundation) instead
// of the Unity default Assembly-CSharp. See docs/tech-debt-register.md TD-002.
[assembly: InternalsVisibleTo("IronGrind.Foundation.EditModeTests")]
