namespace ProjectEnchanter;

/// <summary>Something the player can look at and interact with using E.</summary>
public interface IInteractable
{
    /// <summary>Text shown in the HUD when the player is looking at this object.</summary>
    string InteractionPrompt { get; }

    /// <summary>Called when the player presses the interact key.</summary>
    void Interact(PlayerController player);
}
