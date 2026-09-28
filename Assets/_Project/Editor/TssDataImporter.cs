using System.IO;
using SuitsSim.Tss.Embedded;
using UnityEditor;
using UnityEngine;

namespace SuitsSim.EditorTools
{
    /// <summary>
    /// Copies the seven files EmbeddedTss needs out of a TSS2026 clone into
    /// Assets/_Project/TssData and assigns them to every EmbeddedTss in the open scene.
    /// TssData is gitignored: the TSS repo has no license file, so its files stay out of yours.
    /// </summary>
    public static class TssDataImporter
    {
        const string Target = "Assets/_Project/TssData";

        static readonly (string from, string to, string field)[] Files =
        {
            ("src/lib/simulation/config/eva1.json", "eva1.json", "eva1Config"),
            ("src/lib/simulation/config/eva2.json", "eva2.json", "eva2Config"),
            ("src/lib/simulation/config/rover.json", "rover.json", "roverConfig"),
            ("data/EVA.json", "EVA.json", "evaJson"),
            ("data/ROVER.json", "ROVER.json", "roverJson"),
            ("data/LTV.json", "LTV.json", "ltvJson"),
            ("data/LTV_ERRORS.json", "LTV_ERRORS.json", "ltvErrorsJson"),
        };

        [MenuItem("SUITS Sim/Import TSS Data...")]
        public static void Import()
        {
            var root = EditorUtility.OpenFolderPanel("Select your TSS2026 clone (the folder with build.bat)", "", "");
            if (string.IsNullOrEmpty(root)) return;

            foreach (var f in Files)
            {
                if (File.Exists(Path.Combine(root, f.from))) continue;
                EditorUtility.DisplayDialog("Import TSS Data",
                    $"{f.from} is missing from that folder.\n\nClone github.com/SUITS-Techteam/TSS2026 and pick its root folder. " +
                    "Run `git checkout data` first if you've run the server, so the data files are pristine.", "OK");
                return;
            }

            Directory.CreateDirectory(Target);
            foreach (var f in Files) File.Copy(Path.Combine(root, f.from), Path.Combine(Target, f.to), true);
            AssetDatabase.Refresh();
            int assigned = AssignToScene();
            Debug.Log($"[TSS import] copied {Files.Length} files to {Target}; assigned to {assigned} EmbeddedTss component(s).");
        }

        [MenuItem("SUITS Sim/Assign TSS Data To Scene")]
        public static int AssignToScene()
        {
            int count = 0;
            foreach (var tss in Object.FindObjectsByType<EmbeddedTss>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(tss);
                foreach (var f in Files)
                    so.FindProperty(f.field).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Target}/{f.to}");
                so.ApplyModifiedProperties();
                count++;
            }
            return count;
        }
    }
}
