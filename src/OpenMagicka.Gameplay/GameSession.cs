using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay;

/// <summary>The kind of game, as the original numbers them (docs/gameplay/MAGICKS.md §1).</summary>
public enum GameType
{
    Campaign = 1,
    Challenge = 2,
    Versus = 4,
    Mythos = 8,
    StoryChallenge = 22,
}

/// <summary>A player of the game, one of up to four, with the magicks it has unlocked (MAGICKS.md §1).</summary>
public sealed class Player
{
    internal Player(int id) => Id = id;

    public int Id { get; }

    /// <summary>Whether the player takes part (a slot can be empty).</summary>
    public bool Playing { get; set; } = true;

    /// <summary>Bit <c>1 &lt;&lt; magick</c> for each unlocked magick.</summary>
    public ulong UnlockedMagicks { get; set; }

    /// <summary>The player's wizard in the level, alive or dead; null when it has none.</summary>
    public Character? Avatar { get; set; }

    /// <summary>The magick the player has picked in its book (the HUD shows it; the wheel or the pad change it).</summary>
    public MagickType SelectedMagick { get; set; }

    /// <summary>A revival is on its way to this player.</summary>
    public bool Ressing { get; internal set; }

    /// <summary>How long its avatar has been dead (0 while it lives).</summary>
    public float DeadAge { get; internal set; }

    public bool HasUnlocked(MagickType magick) => (UnlockedMagicks & Bit(magick)) != 0;

    public void Unlock(MagickType magick) => UnlockedMagicks |= Bit(magick);

    internal static ulong Bit(MagickType magick) =>
        magick is > MagickType.None and < MagickType.NrOfMagicks ? 1UL << (int)magick : throw new ArgumentOutOfRangeException(nameof(magick), magick, "not a player magick");

    internal void Tick(float dt) => DeadAge = Avatar is { IsDead: true } ? DeadAge + dt : 0;
}

/// <summary>The game being played: its type and its players (docs/gameplay/MAGICKS.md §1).</summary>
public sealed class GameSession
{
    public const int MaxPlayers = 4;

    private readonly List<Player> players = [];

    public GameSession(GameType type = GameType.Campaign)
    {
        Type = Enum.IsDefined(type) ? type : throw new ArgumentOutOfRangeException(nameof(type), type, "unknown game type");
    }

    public GameType Type { get; }

    public IReadOnlyList<Player> Players => players;

    public Player AddPlayer()
    {
        if (players.Count >= MaxPlayers)
        {
            throw new InvalidOperationException($"a game has at most {MaxPlayers} players");
        }
        var player = new Player(players.Count);
        players.Add(player);
        return player;
    }

    /// <summary>
    /// Whether a magick may be cast. In the campaigns and challenges a magick any playing player
    /// unlocked serves everyone; elsewhere (Versus) each player has its own. A character with no
    /// player (an NPC) may cast any magick.
    /// </summary>
    public bool IsMagickAllowed(Player? player, MagickType magick)
    {
        if (magick is <= MagickType.None or >= MagickType.NrOfMagicks)
        {
            return false;
        }
        if (player is null)
        {
            return true;
        }
        return Type == GameType.Versus ? player.HasUnlocked(magick) : players.Any(p => p.Playing && p.HasUnlocked(magick));
    }

    /// <summary>Unlocks a magick for every player (the trigger action, a book outside Versus).</summary>
    public void UnlockForAll(MagickType magick)
    {
        foreach (var player in players)
        {
            player.Unlock(magick);
        }
    }

    internal void Tick(float dt)
    {
        foreach (var player in players)
        {
            player.Tick(dt);
        }
    }
}
