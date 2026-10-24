using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Domain.Tests;

public sealed class ServiceSpecificationTests
{
    [Fact]
    public void L_identifiant_d_offre_est_normalise_en_majuscules()
    {
        Assert.Equal(new SpecificationId("C2_URLLC"), new SpecificationId(" c2_urllc "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("C2/URLLC")]
    [InlineData("2C")]
    public void Un_identifiant_d_offre_mal_forme_est_refuse(string valeur)
    {
        Assert.Throws<RegleMetierException>(() => new SpecificationId(valeur));
    }

    [Fact]
    public void Une_offre_sans_caracteristique_est_refusee()
    {
        Assert.Throws<RegleMetierException>(() => new ServiceSpecification(new SpecificationId("C2_URLLC"), "C2", "", "1.0", []));
    }

    [Fact]
    public void Une_offre_ne_peut_pas_definir_deux_fois_la_meme_caracteristique()
    {
        Assert.Throws<RegleMetierException>(() => new ServiceSpecification(new SpecificationId("C2_URLLC"), "C2", "", "1.0",
            [new("sst", "2"), new("SST", "1")]));
    }

    [Fact]
    public void Une_caracteristique_doit_avoir_une_valeur()
    {
        Assert.Throws<RegleMetierException>(() => new Caracteristique("sst", " "));
    }
}
