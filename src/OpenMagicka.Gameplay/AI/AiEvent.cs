namespace OpenMagicka.Gameplay.AI;

/// <summary>A scripted step of an order (a move, a clip, a turn, a kill, a loop); they come with phase 5 of OM-40.</summary>
public abstract record AiEvent(float Delay);
