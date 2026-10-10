using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilder.Content
{
    /// <summary>
    /// Reads the scripts a bundled model carries and runs them. A functional asset bundle ships a
    /// prefab with MonoBehaviour components attached; this reader walks the prefab, confirms every
    /// script reference still resolves to a type in this build, and hands each <see cref="IBundleScript"/>
    /// its model's definition so it can behave.
    ///
    /// Scripts cannot travel inside a bundle as code -- the device compiled its C# ahead of time -- so a
    /// prefab records a script only as a reference to a type that must already be in the app. When a
    /// bundle was built against a type this build does not have, that reference comes back empty: Unity
    /// leaves a null where the component should be, the component does nothing, and nothing says why.
    /// <see cref="Read"/> counts those empty slots so the problem surfaces as an error instead of a
    /// silently dead building. See <see cref="IBundleScript"/> for how a model's prefab is set up.
    /// </summary>
    public static class BundleScriptReader
    {
        /// <summary>What the scripts on one model look like: what runs, what is missing, what to drive.</summary>
        public sealed class Report
        {
            /// <summary>Type names of the live MonoBehaviour scripts found on the model and its children.</summary>
            public readonly List<string> Scripts = new List<string>();

            /// <summary>The scripts that take part in the content pipeline, ready to be initialized.</summary>
            public readonly List<IBundleScript> BundleScripts = new List<IBundleScript>();

            /// <summary>
            /// How many component slots held a script this build could not resolve. One per broken
            /// reference, counted across the whole hierarchy.
            /// </summary>
            public int MissingScriptCount;

            /// <summary>Problems worth logging: missing scripts, and any that threw while initializing.</summary>
            public readonly List<string> Errors = new List<string>();

            /// <summary>True when the model carries at least one <see cref="IBundleScript"/> to run.</summary>
            public bool IsFunctional => BundleScripts.Count > 0;

            /// <summary>True when any script reference failed to resolve; the model is not whole.</summary>
            public bool HasMissingScripts => MissingScriptCount > 0;
        }

        /// <summary>
        /// Walks <paramref name="root"/> and everything under it, listing the scripts it carries and
        /// counting any whose type is missing from this build. Does not run them; see <see cref="Initialize"/>.
        /// </summary>
        public static Report Read(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var report = new Report();

            // One GameObject at a time: GetComponents returns a null entry for every component whose
            // script could not be resolved, which is how a missing type shows up at runtime.
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        report.MissingScriptCount++;
                        continue;
                    }

                    if (component is IBundleScript bundleScript)
                    {
                        report.BundleScripts.Add(bundleScript);
                        report.Scripts.Add(component.GetType().Name);
                    }
                    else if (component is MonoBehaviour)
                    {
                        report.Scripts.Add(component.GetType().Name);
                    }
                }
            }

            if (report.HasMissingScripts)
            {
                report.Errors.Add($"Model '{root.name}' has {report.MissingScriptCount} missing script " +
                                  "reference(s): it was built against code this app does not have. " +
                                  "Rebuild the bundle against this version, or update the app.");
            }

            return report;
        }

        /// <summary>
        /// Reads <paramref name="root"/> and then initializes every bundle script on it with
        /// <paramref name="context"/>. A script that throws is caught and noted in the report's errors so
        /// one broken script does not take the rest down. The caller logs the errors.
        /// </summary>
        public static Report Initialize(GameObject root, BundleScriptContext context)
        {
            Report report = Read(root);

            foreach (IBundleScript script in report.BundleScripts)
            {
                try
                {
                    script.Initialize(context);
                }
                catch (Exception error)
                {
                    report.Errors.Add($"Script '{script.GetType().Name}' on model " +
                                      $"'{context.BundleName}/{context.ModelName}' threw while initializing: {error}");
                }
            }

            return report;
        }
    }
}
