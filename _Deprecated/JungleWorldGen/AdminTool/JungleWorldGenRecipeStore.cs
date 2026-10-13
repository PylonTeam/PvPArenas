#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

internal sealed class JungleWorldGenRecipe
{
    public string Name { get; set; } = "";
    public string[] Passes { get; set; } = [];
    public int? FixedSeed { get; set; }
}

/// <summary>Recipes belong to this client and never change server settings until Apply/Generate.</summary>
internal sealed class JungleWorldGenRecipeStore
{
    private readonly string path;
    private readonly List<JungleWorldGenRecipe> recipes = [];
    internal IReadOnlyList<JungleWorldGenRecipe> Recipes => recipes;
    internal JungleWorldGenRecipeStore(string path = null) => this.path = path ?? Path.Combine(Main.SavePath, "ModConfigs", "PvPArenas", "JungleWorldGenRecipes.json");

    internal bool Load(out string error)
    {
        error = "";
        recipes.Clear();
        try
        {
            if (!File.Exists(path)) return true;
            if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Recipe file exceeds 1 MiB.");
            List<JungleWorldGenRecipe> saved = JsonSerializer.Deserialize<List<JungleWorldGenRecipe>>(File.ReadAllText(path)) ?? [];
            foreach (JungleWorldGenRecipe recipe in saved.Take(100))
            {
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 48) continue;
                JungleWorldGenDraft normalized = new();
                normalized.Load(recipe.Passes ?? [], recipe.FixedSeed);
                recipe.Passes = normalized.Passes.ToArray();
                recipes.RemoveAll(existing => existing.Name.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase));
                recipes.Add(recipe);
            }
            Sort();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            recipes.Clear();
            error = $"Cannot load local recipes: {exception.Message}";
            return false;
        }
    }

    internal JungleWorldGenRecipe Find(string name) => recipes.FirstOrDefault(recipe => recipe.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));

    internal bool Save(string name, IReadOnlyList<string> passes, int? seed, out string error)
    {
        error = "";
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 48)
        {
            error = "Give this local recipe a name (1–48 characters).";
            return false;
        }
        if (recipes.Count >= 100 && Find(name) == null)
        {
            error = "The local recipe file already contains 100 recipes.";
            return false;
        }
        try
        {
            JungleWorldGenDraft normalized = new();
            normalized.Load(passes, seed);
            List<JungleWorldGenRecipe> updated = recipes.Where(recipe => !recipe.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            updated.Add(new JungleWorldGenRecipe { Name = name, Passes = normalized.Passes.ToArray(), FixedSeed = seed });
            updated.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
            recipes.Clear();
            recipes.AddRange(updated);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            error = $"Cannot save local recipe: {exception.Message}";
            return false;
        }
    }

    private void Sort() => recipes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
}
#endif
