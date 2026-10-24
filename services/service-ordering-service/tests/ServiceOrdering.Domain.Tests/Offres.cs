using ServiceOrdering.Domain.Catalogue;

namespace ServiceOrdering.Domain.Tests;

/// <summary>Les deux offres du scénario de référence, telles que le catalogue les définit.</summary>
internal static class Offres
{
    public static readonly ServiceSpecification C2 = new(
        new SpecificationId("C2_URLLC"), "C2 / URLLC", "Commande et contrôle", "1.0",
        [new("sst", "2"), new("sd", "000001"), new("dnn", "c2.candronex"), new("5qi", "82")]);

    public static readonly ServiceSpecification Imagerie = new(
        new SpecificationId("IMAGERIE_EMBB"), "Imagerie / eMBB", "Vidéo de mission", "1.0",
        [new("sst", "1"), new("sd", "000002"), new("dnn", "imagerie.candronex"), new("5qi", "4")]);
}
