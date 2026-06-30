namespace DigitalBrain.Tests.Authoring;

// Copy-me starter content bundle. The Code string is the SINGLE source of truth:
// the fast test (BundleHarness) and the render E2E (LiveRenderVerifier) both compile it.
// To make your own bundle: copy this file, rename the type/ids, change the hops.
public static class StarterBundleSource
{
    public const string Pack = "starter";
    public const string ExperienceId = "starter";

    public static class Hops
    {
        public const string Ask = "ask";
        public const string Result = "result";
    }

    public const string Code = """
using System.Collections.Generic;
using DigitalBrain.Core;

public sealed class StarterExperience : KitExperience
{
    protected override UiExperience Define() => Experience("starter", "Starter Bundle")
        .Hop("ask", s => s
            .Text("What should I echo?")
            .TextField("message", "Your message")
            .Button("Echo", "result"))
        .Hop("result", s => s
            .Panel(p => p.Text(state =>
                "You said: " + (state.GetValueOrDefault("message") is { Length: > 0 } m ? m : "nothing"))));
}
""";
}
