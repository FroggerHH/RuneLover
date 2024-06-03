using System.Reflection;

// ReSharper disable InconsistentNaming

namespace RuneLover.Managers;

public static class AnimationSpeedManager
{
    private static readonly Harmony harmony = new("AnimationSpeedManager");
    private static bool hasMarkerPatch;

    public delegate double Handler(Character character, double speed);

    private static readonly MethodInfo method =
        AccessTools.DeclaredMethod(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate));

    private static int index;
    private static bool changed;
    private static Handler[][] handlers = [];
    private static readonly Dictionary<int, List<Handler>> handlersPriorities = new();

    [PublicAPI]
    public static void Add(Handler handler, int priority = Priority.Normal)
    {
        if (!hasMarkerPatch)
        {
            harmony.Patch(method,
                finalizer: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AnimationSpeedManager),
                    nameof(markerPatch))));
            hasMarkerPatch = true;
        }

        if (!handlersPriorities.TryGetValue(priority, out var priorityHandlers))
        {
            handlersPriorities.Add(priority, priorityHandlers = []);
            harmony.Patch(method,
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(AnimationSpeedManager), nameof(wrapper))));
        }

        priorityHandlers.Add(handler);

        handlers = handlersPriorities.OrderBy(kv => kv.Key).Select(kv => kv.Value.ToArray()).ToArray();
    }

    private static void wrapper(Character ___m_character, Animator ___m_animator)
    {
        var currentSpeedMarker = ___m_animator.speed * 1e7 % 100;
        if (currentSpeedMarker is > 10 and < 30 || ___m_animator.speed <= 0.001f)
        {
            return;
        }

        double speed = ___m_animator.speed;
        var newSpeed = handlers[index++].Aggregate(speed, (current, handler) => handler(___m_character, current));
        if (Math.Abs(newSpeed - speed) > 0.01f)
        {
            ___m_animator.speed = (float)(newSpeed - newSpeed % 1e-5);
            changed = true;
        }
    }

    private static void markerPatch(Animator ___m_animator)
    {
        if (changed)
        {
            var speed = ___m_animator.speed;
            var currentSpeedMarker = speed * 1e7 % 100;
            if (currentSpeedMarker is < 10 or > 30)
            {
                ___m_animator.speed += 19e-7f;
            }

            changed = false;
        }

        index = 0;
    }
}