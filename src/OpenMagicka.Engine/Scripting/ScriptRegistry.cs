using System.Globalization;
using System.Numerics;
using System.Reflection;
using OpenMagicka.Content.Levels;

namespace OpenMagicka.Engine.Scripting;

/// <summary>
/// The kinds of conditions and actions by tag (any case), and the original's binding of a node's
/// attributes (LEVELS_TRIGGERS.md §2.2, §5.3): each to the public settable property of that name
/// (any case), unknown ones skipped. Strings are lowered; enums by name in any case, a number or a
/// comma list of flags; bools as .NET reads them; numbers and vectors (comma lists) in the invariant
/// culture. A property of another type is an error.
/// </summary>
public sealed class ScriptRegistry
{
    private readonly Dictionary<string, Type> conditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Type> actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> unknown = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> Settable = new();

    /// <summary>
    /// The engine's own kinds, every one the original has (LEVELS_TRIGGERS.md §9): all the conditions;
    /// counters, timers, executing and clearing triggers, points of interest; the characters' actions
    /// (spawn, orders, avatars' events, moves, removal, kills, damage, factions, teleports, magicks, items),
    /// the things' (animated parts, effects, items, props, books, eggs, dispensers), the stage's (camera,
    /// fade, cutscene, input, lights, the HUD, the tutorial, and the requests for sound, dialogs, titles,
    /// checkpoints, scene changes and the level's end), the profile's and the boss fights'.
    /// </summary>
    public static ScriptRegistry Engine() => new ScriptRegistry()
        .Condition<CounterCondition>("counter")
        .Condition<TimerCondition>("timer")
        .Condition<PresentCondition>("present")
        .Condition<FactionPresentCondition>("factionPresent")
        .Condition<IdPresentCondition>("idPresent")
        .Condition<PhysicsPresentCondition>("physicsPresent")
        .Condition<TriggeredCondition>("triggered")
        .Condition<CharacterHealthCondition>("characterHealth")
        .Condition<InteractorIsCondition>("interactorIs")
        .Condition<CharacterCastingCondition>("characterCasting")
        .Condition<RobeInUseCondition>("robeInUse")
        .Condition<PlayerCountCondition>("playerCount")
        .Condition<PlayersHaveStatusEffectCondition>("playersHaveStatusEffect")
        .Condition<PlayerHasElementCondition>("playerHasElement")
        .Condition<DialogDoneCondition>("dialogDone")
        .Condition<DialogActiveCondition>("dialogActive")
        .Condition<DialogHintDoneCondition>("dialogHintDone")
        .Condition<BossHealthCondition>("bossHealth")
        .Condition<BossDeadCondition>("bossDead")
        .Condition<NoCutsceneActiveCondition>("noCutsceneActive")
        .Condition<HasAchievementCondition>("hasAchievement")
        .Condition<AnyPlayerHasDiedCondition>("anyPlayerHasDied")
        .Condition<HasDlcCondition>("hasDLC")
        .Condition<IsCampaignLoopedCondition>("isCampaignLooped")
        .Action<CounterAction>("counter")
        .Action<TimerAction>("timer")
        .Action<AddTimerAction>("addTimer")
        .Action<ExecuteTriggerAction>("executeTrigger")
        .Action<ClearDelayedActionsAction>("clearDelayedActions")
        .Action<EnablePoiAction>("enablePoi")
        .Action<DisablePoiAction>("disablePoi")
        .Action<SpawnAction>("spawn")
        .Action<GiveOrderAction>("giveOrder")
        .Action<AvatarMoveAction>("avatarMove")
        .Action<SetPositionAction>("setPosition")
        .Action<RemoveAction>("remove")
        .Action<PlayAnimationAction>("playAnimation")
        .Action<SpawnEffectAction>("spawnEffect")
        .Action<StartEffectAction>("startEffect")
        .Action<StopEffectAction>("stopEffect")
        .Action<SpawnItemAction>("spawnItem")
        .Action<SpawnPhysAction>("spawnPhys")
        .Action<CameraMoveAction>("cameraMove")
        .Action<CameraReleaseAction>("cameraRelease")
        .Action<CameraShakeAction>("cameraShake")
        .Action<CameraMagnifyAction>("cameraMagnify")
        .Action<FadeAction>("fade")
        .Action<CutsceneAction>("cutscene")
        .Action<EndCutsceneAction>("endCutscene")
        .Action<DisableInputAction>("disableInput")
        .Action<EnableInputAction>("enableInput")
        .Action<PlaySoundAction>("playSound")
        .Action<StopSoundAction>("stopSound")
        .Action<PlayMusicAction>("playMusic")
        .Action<StartDialogAction>("startDialog")
        .Action<TriggerCheckpointAction>("triggerCheckpoint")
        .Action<ChangeSceneAction>("changeScene")
        .Action<EnterSceneAction>("enterScene")
        .Action<KillAction>("kill")
        .Action<OverkillAction>("overkill")
        .Action<DamageAction>("damage")
        .Action<SetFactionAction>("setFaction")
        .Action<RemoveAllStatusEffectsAction>("removeAllStatusEffects")
        .Action<TeleportAction>("teleport")
        .Action<MagickAction>("magick")
        .Action<UnlockMagickAction>("unlockMagick")
        .Action<SpawnMagickAction>("spawnMagick")
        .Action<SpawnElementalAction>("spawnElemental")
        .Action<AssignItemAction>("assignItem")
        .Action<AddItemToAvatarsAction>("addItemToAvatars")
        .Action<RemoveItemFromAvatarsAction>("removeItemFromAvatars")
        .Action<SetDialogAction>("setDialog")
        .Action<StopAnimationAction>("stopAnimation")
        .Action<ResumeAnimationAction>("resumeAnimation")
        .Action<HighlightAnimAction>("highlightAnim")
        .Action<HighlightPhysAction>("highlightPhys")
        .Action<SpawnDispenserAction>("spawnDispenser")
        .Action<ActivateDispenserAction>("activateDispenser")
        .Action<SpawnAnimPropAction>("spawnAnimProp")
        .Action<SpawnCloneWizardsAction>("spawnCloneWizards")
        .Action<EnableElementAction>("enableElement")
        .Action<DisableElementAction>("disableElement")
        .Action<EnableCastTypeAction>("enableCastType")
        .Action<DisableCastTypeAction>("disableCastType")
        .Action<EnablePushAction>("enablePush")
        .Action<DisablePushAction>("disablePush")
        .Action<EnableDarkAction>("enableDark")
        .Action<DisableDarkAction>("disableDark")
        .Action<SetHintAction>("setHint")
        .Action<RemoveHintAction>("removeHint")
        .Action<SetDialogHintAction>("setDialogHint")
        .Action<RemoveDialogHintAction>("removeDialogHint")
        .Action<NothingAction>("telemetryTutorialBegin")
        .Action<NothingAction>("telemetryTutorialStep")
        .Action<NothingAction>("telemetryTutorialEnd")
        .Action<NothingAction>("telemetryTutorialSkip")
        .Action<NothingAction>("printDebug")
        .Action<DisableLightAction>("disableLight")
        .Action<EnableLightAction>("enableLight")
        .Action<CameraLockAction>("cameraLock")
        .Action<CameraBiasAction>("cameraBias")
        .Action<CameraFollowAction>("cameraFollow")
        .Action<CameraPositionAction>("cameraPosition")
        .Action<AddCameraEffectAction>("addCameraEffect")
        .Action<RemoveCameraEffectAction>("removeCameraEffect")
        .Action<FlashAction>("flash")
        .Action<StopMusicAction>("stopMusic")
        .Action<SetMusicFocusAction>("setMusicFocus")
        .Action<SetVolumeAction>("setVolume")
        .Action<DisplayTitleInLevelAction>("displayTitleInLevel")
        .Action<BossTitleAction>("bossTitle")
        .Action<HealthBarAction>("healthBar")
        .Action<ActivateHealthBarAction>("activateHealthBar")
        .Action<RemoveHealthBarAction>("removeHealthBar")
        .Action<ScrollScoreAction>("scrollScore")
        .Action<RollCreditsAction>("rollCredits")
        .Action<EndGameAction>("endGame")
        .Action<DisableUiAction>("disableUI")
        .Action<EnableUiAction>("enableUI")
        .Action<KillDialogsAction>("killDialogs")
        .Action<BeginDefaultAvatarSectionAction>("beginDefaultAvatarSection")
        .Action<EndDefaultAvatarSectionAction>("endDefaultAvatarSection")
        .Action<BeginUnequipWeaponsSectionAction>("beginUnequipWeaponsSection")
        .Action<EndUnequipWeaponsSectionAction>("endUnequipWeaponsSection")
        .Action<UnlockAchievementAction>("unlockAchievement")
        .Action<ConditionalUnlockAchievementAction>("conditionalUnlockAchievement")
        .Action<FoundSecretAreaAction>("foundSecretArea")
        .Action<FoundMooseAction>("foundMoose")
        .Action<KilledAPonyAction>("killedAPony")
        .Action<WizardControlAction>("wizardControl")
        .Action<ReleaseGripAction>("releaseGrip")
        .Action<PhysAnimationAction>("physAnimation")
        .Action<SpawnBossAction>("spawnBoss")
        .Action<SpawnGenericBossAction>("spawnGenericBoss")
        .Action<ActivateBossAction>("activateBoss")
        .Action<EndBossFightAction>("endBossFight")
        .Action<BossMessageAction>("bossMessage");

    /// <summary>The tags met that no kind answers (they never hold, or do nothing).</summary>
    public IReadOnlyCollection<string> Unknown => unknown;

    public ScriptRegistry Condition<T>(string tag) where T : ScriptCondition, new()
    {
        conditions[tag] = typeof(T);
        return this;
    }

    public ScriptRegistry Action<T>(string tag) where T : ScriptAction, new()
    {
        actions[tag] = typeof(T);
        return this;
    }

    public bool Knows(string tag) => conditions.ContainsKey(tag) || actions.ContainsKey(tag);

    internal ScriptCondition MakeCondition(ScriptNode node)
    {
        if (!conditions.TryGetValue(node.Name, out var type))
        {
            unknown.Add("if:" + node.Name.ToLowerInvariant());
            return new UnknownCondition(node.Name);
        }
        return Bind((ScriptCondition)Activator.CreateInstance(type)!, node);
    }

    internal ScriptAction MakeAction(ScriptNode node)
    {
        if (!actions.TryGetValue(node.Name, out var type))
        {
            unknown.Add("then:" + node.Name.ToLowerInvariant());
            return new UnknownAction(node.Name);
        }
        var action = Bind((ScriptAction)Activator.CreateInstance(type)!, node);
        action.Node = node;
        return action;
    }

    /// <summary>Sets the node's attributes on the part's properties, in the node's order.</summary>
    public static T Bind<T>(T part, ScriptNode node) where T : notnull
    {
        var properties = Settable.GetOrAdd(part.GetType(), PropertiesOf);
        foreach (var attribute in node.Attributes)
        {
            if (!properties.TryGetValue(attribute.Name, out var property))
            {
                continue;                                                     // as the original: an unknown attribute is skipped
            }
            try
            {
                property.SetValue(part, Value(property.PropertyType, attribute.Value));
            }
            catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException or TargetInvocationException)
            {
                throw new InvalidDataException($"<{node.Name} {attribute.Name}=\"{attribute.Value}\">: {(e.InnerException ?? e).Message}", e);
            }
        }
        return part;
    }

    /// <summary>A kind's public settable properties by name (any case), the most derived where a name is hidden.</summary>
    private static Dictionary<string, PropertyInfo> PropertiesOf(Type type)
    {
        var found = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is { IsPublic: true }))
        {
            if (!found.TryGetValue(property.Name, out var known) || property.DeclaringType!.IsSubclassOf(known.DeclaringType!))
            {
                found[property.Name] = property;
            }
        }
        return found;
    }

    private static object Value(Type type, string text)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum)
        {
            return Enum.Parse(target, text, ignoreCase: true);
        }
        if (target == typeof(string))
        {
            return text.ToLowerInvariant();
        }
        if (target == typeof(bool))
        {
            return bool.Parse(text);
        }
        if (target == typeof(int))
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }
        if (target == typeof(float))
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }
        var parts = target == typeof(Vector2) || target == typeof(Vector3) || target == typeof(Vector4)
            ? text.Split(',').Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray()
            : null;
        if (target == typeof(Vector2))
        {
            return new Vector2(parts![0], parts[1]);
        }
        if (target == typeof(Vector3))
        {
            return new Vector3(parts![0], parts[1], parts[2]);
        }
        if (target == typeof(Vector4))
        {
            return new Vector4(parts![0], parts[1], parts[2], parts[3]);
        }
        throw new NotSupportedException($"a script property of type {type.Name}");
    }
}
