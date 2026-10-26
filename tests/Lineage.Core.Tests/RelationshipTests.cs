using Lineage.Core.Analysis;
using Lineage.Core.Model;
using static Lineage.Core.Tests.AnalysisTestData;

namespace Lineage.Core.Tests;

public class RelationshipTests
{
    /// <summary>
    /// An invented five-generation family:
    /// Albert+Alice → Bernard, Beatrice. Bernard+Bella → Charles, Clara, Clive. Beatrice+Brian → Cecil.
    /// Charles+Cathy → David, Dora, Sam. Clara+Colm → Colin. Cecil+Cora → Derek.
    /// David+Diana → Edward; David+Fiona → Felix; Fiona+Gus → Greta. Dora+Dan → Emma. Derek+Daisy → Eric.
    /// Eric+Erin → Frank. Edward+Ellen; Harold+Hilda → Ellen, Iris. Zed is nobody's relative.
    /// </summary>
    private sealed class Family5
    {
        public readonly FamilyTree T = new();
        public readonly Person Albert, Alice, Bernard, Beatrice, Bella, Brian, Charles, Clara, Clive, Cecil, Cathy, Colm, Cora,
            David, Dora, Sam, Colin, Derek, Diana, Fiona, Gus, Dan, Daisy, Edward, Felix, Greta, Emma, Eric, Erin, Frank,
            Ellen, Harold, Hilda, Iris, Zed;

        public Family5()
        {
            Person M(string name) => P(T, name, "Example", Sex.Male);
            Person F(string name) => P(T, name, "Example", Sex.Female);
            Albert = M("Albert"); Alice = F("Alice"); Bernard = M("Bernard"); Beatrice = F("Beatrice");
            Bella = F("Bella"); Brian = M("Brian"); Charles = M("Charles"); Clara = F("Clara"); Clive = M("Clive"); Cecil = M("Cecil");
            Cathy = F("Cathy"); Colm = M("Colm"); Cora = F("Cora"); David = M("David"); Dora = F("Dora");
            Sam = P(T, "Sam", "Example"); Colin = M("Colin"); Derek = M("Derek"); Diana = F("Diana"); Fiona = F("Fiona");
            Gus = M("Gus"); Dan = M("Dan"); Daisy = F("Daisy"); Edward = M("Edward"); Felix = M("Felix");
            Greta = F("Greta"); Emma = F("Emma"); Eric = M("Eric"); Erin = F("Erin"); Frank = M("Frank");
            Ellen = F("Ellen"); Harold = M("Harold"); Hilda = F("Hilda"); Iris = F("Iris"); Zed = M("Zed");

            Couple(T, Albert, Alice, Bernard, Beatrice);
            Couple(T, Bernard, Bella, Charles, Clara, Clive);
            Couple(T, Brian, Beatrice, Cecil);
            Couple(T, Charles, Cathy, David, Dora, Sam);
            Couple(T, Colm, Clara, Colin);
            Couple(T, Cecil, Cora, Derek);
            Couple(T, David, Diana, Edward);
            Couple(T, David, Fiona, Felix);
            Couple(T, Gus, Fiona, Greta);
            Couple(T, Dan, Dora, Emma);
            Couple(T, Derek, Daisy, Eric);
            Couple(T, Eric, Erin, Frank);
            Couple(T, Edward, Ellen);
            Couple(T, Harold, Hilda, Ellen, Iris);
        }

        public string What(Person a, Person b) => Relationship.Describe(T, a, b).Text;
    }

    private readonly Family5 _f = new();

    [Fact]
    public void DirectLine()
    {
        var f = _f;
        Assert.Equal("father", f.What(f.Edward, f.David));
        Assert.Equal("mother", f.What(f.Edward, f.Diana));
        Assert.Equal("son", f.What(f.David, f.Edward));
        Assert.Equal("grandfather", f.What(f.Edward, f.Charles));
        Assert.Equal("grandson", f.What(f.Charles, f.Edward));
        Assert.Equal("great-grandfather", f.What(f.Edward, f.Bernard));
        Assert.Equal("2nd great-grandfather", f.What(f.Edward, f.Albert));
        Assert.Equal("2nd great-grandmother", f.What(f.Edward, f.Alice));
        Assert.Equal("2nd great-grandson", f.What(f.Albert, f.Edward));
        Assert.Equal("3rd great-grandson", f.What(f.Albert, f.Frank));
    }

    [Fact]
    public void SiblingsAuntsUnclesNiecesAndNephews()
    {
        var f = _f;
        Assert.Equal("sister", f.What(f.David, f.Dora));
        Assert.Equal("brother", f.What(f.Dora, f.David));
        Assert.Equal("sibling", f.What(f.David, f.Sam));
        Assert.Equal("half-brother", f.What(f.Edward, f.Felix));
        Assert.Equal("half-brother", f.What(f.Felix, f.Edward));
        Assert.Equal("aunt", f.What(f.Edward, f.Dora));
        Assert.Equal("uncle or aunt", f.What(f.Edward, f.Sam));
        Assert.Equal("nephew", f.What(f.Dora, f.Edward));
        Assert.Equal("niece", f.What(f.David, f.Emma));
        Assert.Equal("great-aunt", f.What(f.Edward, f.Clara));
        Assert.Equal("great-uncle", f.What(f.Edward, f.Clive));
        Assert.Equal("great-nephew", f.What(f.Clara, f.Edward));
        Assert.Equal("great-nephew", f.What(f.Clive, f.Edward));
        Assert.Equal("2nd great-aunt", f.What(f.Edward, f.Beatrice));
        Assert.Equal("2nd great-aunt's husband", f.What(f.Edward, f.Brian));
    }

    [Fact]
    public void Cousins()
    {
        var f = _f;
        Assert.Equal("first cousin", f.What(f.Edward, f.Emma));
        Assert.Equal("first cousin once removed", f.What(f.Edward, f.Colin));
        Assert.Equal("first cousin once removed", f.What(f.Colin, f.Edward));
        Assert.Equal("second cousin once removed", f.What(f.Edward, f.Derek));
        Assert.Equal("second cousin twice removed", f.What(f.David, f.Frank));
        Assert.Equal("third cousin", f.What(f.Edward, f.Eric));
        Assert.Equal("third cousin once removed", f.What(f.Edward, f.Frank));
        Assert.Equal("first cousin three times removed", f.What(f.Charles, f.Frank));
    }

    [Fact]
    public void PathAndCommonAncestors()
    {
        var f = _f;
        var r = Relationship.Describe(f.T, f.Edward, f.Emma);
        Assert.True(r.Related);
        Assert.False(r.ByMarriage);
        Assert.Equal(new[] { f.Edward.Id, f.David.Id, f.Charles.Id, f.Dora.Id, f.Emma.Id }, r.Path);
        Assert.Equal(new[] { f.Charles.Id, f.Cathy.Id }, r.CommonAncestors.Order());
        Assert.Equal(new[] { f.David.Id }, Relationship.Describe(f.T, f.Edward, f.Felix).CommonAncestors);
        Assert.Equal(new[] { f.Edward.Id, f.David.Id, f.Charles.Id, f.Bernard.Id, f.Albert.Id },
            Relationship.Describe(f.T, f.Edward, f.Albert).Path);
    }

    [Fact]
    public void SpousesInLawsAndStepRelatives()
    {
        var f = _f;
        Assert.Equal("wife", f.What(f.Edward, f.Ellen));
        Assert.Equal("husband", f.What(f.Ellen, f.Edward));
        Assert.Equal("father-in-law", f.What(f.Edward, f.Harold));
        Assert.Equal("mother-in-law", f.What(f.Edward, f.Hilda));
        Assert.Equal("son-in-law", f.What(f.Harold, f.Edward));
        Assert.Equal("sister-in-law", f.What(f.Edward, f.Iris));
        Assert.Equal("brother-in-law", f.What(f.Iris, f.Edward));
        Assert.Equal("step-mother", f.What(f.Edward, f.Fiona));
        Assert.Equal("step-son", f.What(f.Fiona, f.Edward));
        Assert.Equal("step-sister", f.What(f.Edward, f.Greta));
        Assert.Equal("aunt's husband", f.What(f.Edward, f.Dan));
        Assert.Equal("husband's aunt", f.What(f.Ellen, f.Dora));

        var r = Relationship.Describe(f.T, f.Edward, f.Harold);
        Assert.True(r.ByMarriage);
        Assert.Equal(new[] { f.Edward.Id, f.Ellen.Id, f.Harold.Id }, r.Path);
    }

    [Fact]
    public void SamePersonAndUnrelated()
    {
        var f = _f;
        Assert.Equal("Same person", f.What(f.Edward, f.Edward));
        var r = Relationship.Describe(f.T, f.Edward, f.Zed);
        Assert.False(r.Related);
        Assert.Equal("Not related by blood or marriage that Lineage Roots can find", r.Text);
        Assert.Empty(r.Path);
        Assert.Equal(Relationship.NotRelated, f.What(f.Harold, f.Albert));
    }

    [Fact]
    public void BloodIsPreferredOverMarriage()
    {
        var t = new FamilyTree();
        var gp = P(t, "Gramps", "Holt", Sex.Male);
        var gm = P(t, "Granny", "Holt", Sex.Female);
        var a = P(t, "Arthur", "Holt", Sex.Male);
        var b = P(t, "Bridget", "Holt", Sex.Female);
        Couple(t, gp, gm, a, b);
        var c1 = P(t, "Cousin", "Holt", Sex.Male);
        var c2 = P(t, "Cousin", "Marsh", Sex.Female);
        Couple(t, a, null, c1);
        Couple(t, null, b, c2);
        Couple(t, c1, c2);                                                  // first cousins marry
        Assert.Equal("first cousin", Relationship.Describe(t, c1, c2).Text);
        Assert.False(Relationship.Describe(t, c1, c2).ByMarriage);
    }

    [Fact]
    public void AdoptiveParentsAreNamed()
    {
        var t = new FamilyTree();
        var mum = P(t, "Ruth", "Hale", Sex.Female);
        var kid = P(t, "Toby", "Hale", Sex.Male);
        var f = Couple(t, null, mum);
        t.AddChild(f, kid, new ChildLink { ChildId = kid.Id, ToMother = ParentRelation.Adopted });
        Assert.Equal("adoptive mother", Relationship.Describe(t, kid, mum).Text);
        Assert.Equal("adopted son", Relationship.Describe(t, mum, kid).Text);
    }
}
