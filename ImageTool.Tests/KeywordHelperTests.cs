using System.Collections.Generic;
using System.Linq;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class KeywordHelperTests
{
    [Theory]
    [InlineData("  Animal / Dog ", "Animal/Dog")]
    [InlineData("Animal//Dog", "Animal/Dog")]
    [InlineData("///", null)]
    [InlineData("", null)]
    [InlineData("Tree", "Tree")]
    public void Normalize_Works(string input, string? expected)
    {
        Assert.Equal(expected, KeywordHelper.Normalize(input));
    }

    [Fact]
    public void NormalizeList_DedupesCaseInsensitive()
    {
        var result = KeywordHelper.NormalizeList(new[] { "Dog", "dog", " DOG ", "Cat" });
        Assert.Equal(2, result.Count);
        Assert.Contains("Dog", result);
        Assert.Contains("Cat", result);
    }

    [Fact]
    public void ExpandAncestors_BuildsChain()
    {
        var result = KeywordHelper.ExpandAncestors("Animal/Dog/Puppy");
        Assert.Equal(new[] { "Animal", "Animal/Dog", "Animal/Dog/Puppy" }, result.ToArray());
    }

    [Fact]
    public void LeafName_ReturnsLastSegment()
    {
        Assert.Equal("Puppy", KeywordHelper.LeafName("Animal/Dog/Puppy"));
        Assert.Equal("Tree", KeywordHelper.LeafName("Tree"));
    }

    [Fact]
    public void Matches_BranchPrefix()
    {
        var kws = new[] { "Animal/Dog/Puppy" };
        Assert.True(KeywordHelper.Matches(kws, "Animal"));        // tổ tiên
        Assert.True(KeywordHelper.Matches(kws, "Animal/Dog"));    // nhánh giữa
        Assert.True(KeywordHelper.Matches(kws, "Animal/Dog/Puppy")); // chính xác
    }

    [Fact]
    public void Matches_SingleSegment_CaseInsensitive()
    {
        var kws = new[] { "Animal/Dog" };
        Assert.True(KeywordHelper.Matches(kws, "dog"));   // segment lá
        Assert.True(KeywordHelper.Matches(kws, "ANIMAL"));// segment cha
        Assert.False(KeywordHelper.Matches(kws, "cat"));
    }

    [Fact]
    public void Matches_NoFalsePositiveOnPartialSegment()
    {
        var kws = new[] { "Animal/Dog" };
        // "Ani" không phải segment đầy đủ -> không khớp.
        Assert.False(KeywordHelper.Matches(kws, "Ani"));
    }

    [Fact]
    public void BuildTree_HierarchyAndCounts()
    {
        var counts = new List<KeyValuePair<string, int>>
        {
            new("Animal/Dog", 3),
            new("Animal/Cat", 2),
            new("Place/Beach", 1),
        };
        var tree = KeywordHelper.BuildTree(counts);

        // 2 gốc: Animal, Place (sắp theo tên).
        Assert.Equal(2, tree.Count);
        Assert.Equal("Animal", tree[0].Name);
        Assert.Equal("Place", tree[1].Name);

        // Animal count = 3 + 2 = 5 (cộng cả con).
        Assert.Equal(5, tree[0].Count);
        Assert.Equal(2, tree[0].Children.Count);
        // con sắp theo tên: Cat trước Dog.
        Assert.Equal("Cat", tree[0].Children[0].Name);
        Assert.Equal("Dog", tree[0].Children[1].Name);
        Assert.Equal(2, tree[0].Children[0].Count);
        Assert.Equal(3, tree[0].Children[1].Count);
    }

    [Fact]
    public void BuildTree_DeepHierarchy()
    {
        var counts = new List<KeyValuePair<string, int>> { new("A/B/C", 4) };
        var tree = KeywordHelper.BuildTree(counts);
        Assert.Single(tree);
        Assert.Equal("A", tree[0].Name);
        Assert.Equal(4, tree[0].Count);
        Assert.Single(tree[0].Children);
        Assert.Equal("B", tree[0].Children[0].Name);
        Assert.Equal("C", tree[0].Children[0].Children[0].Name);
        Assert.Equal(4, tree[0].Children[0].Children[0].Count);
    }
}
