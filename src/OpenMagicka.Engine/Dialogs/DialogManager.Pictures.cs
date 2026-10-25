using OpenMagicka.Engine.Rendering;
using OpenMagicka.Gameplay.Characters;
using Xna = Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Dialogs;

public sealed partial class DialogManager
{
    /// <summary>The boxes shown, into the frame's pictures: each slot's, and the cutscene's while it plays.</summary>
    public void Write(GuiPictures into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var slot = 0; slot < Slots; slot++)
        {
            if (!(slot == cutsceneSlot && ShowsCutsceneBox) && boxes[slot].IsVisible)
            {
                into.Boxes.Add(Picture(boxes[slot], cutscene: false));
            }
        }
        if (ShowsCutsceneBox && CutsceneBox.IsVisible)
        {
            into.Boxes.Add(Picture(CutsceneBox, cutscene: true));
        }
    }

    /// <summary>A box's picture: over its speaker's head (a character's capsule top), its point, or the screen's point.</summary>
    public static SpeechBoxPicture Picture(SpeechBox box, bool cutscene)
    {
        ArgumentNullException.ThrowIfNull(box);
        var anchor = box.Anchor;
        System.Numerics.Vector3? at = anchor.Owner switch
        {
            Character character => character.Position + System.Numerics.Vector3.UnitY * (character.Template.Length / 2 + character.Template.Radius),
            { } entity => entity.Position,
            null when anchor.OnScreen is null => anchor.At ?? System.Numerics.Vector3.Zero,
            _ => null,
        };
        return new SpeechBoxPicture(box.Text.Text, box.Text.Visible, box.Name, box.Scale, at is { } p ? new Xna.Vector3(p.X, p.Y, p.Z) : null,
            anchor.OnScreen is { } s ? new Xna.Vector2(s.X, s.Y) : null, new Xna.Point(box.MinSize.Width, box.MinSize.Height), box.ForceOnScreen, cutscene);
    }
}
