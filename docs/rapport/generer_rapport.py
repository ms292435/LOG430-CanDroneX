"""Génère le rapport de la Phase 2 en remplissant le gabarit Word fourni.

Le contenu est rédigé dans docs/rapport/rapport.md, par blocs :

    @@ 1.1 Le mandat de la Phase 2        remplace les consignes sous ce titre (jusqu'au titre suivant)
    @@! 9. Décisions d'architecture — ADR  remplace tout le chapitre (sous-titres du gabarit compris)
    @@- Comment utiliser ce gabarit        supprime ce titre et son contenu
    @@ page de titre                       remplit le tableau de la page de titre (lignes « Champ : valeur »)

Dans un bloc : paragraphes séparés par une ligne vide, « ## » et « ### » pour des sous-titres, « - » et « 1. »
pour des listes, tableaux Markdown (« | a | b | », la ligne de séparation peut porter les largeurs relatives :
« |---:30|---:70| »), images « ![légende](chemin relatif à ce fichier) », blocs de code ```, et en ligne
**gras**, *italique* et `code`. « [[SOMMAIRE]] » insère une table des matières (à mettre à jour dans Word).

Les sections encore absentes de rapport.md gardent leurs consignes : le script liste ce qui reste à rédiger.

Usage : python docs/rapport/generer_rapport.py [sortie.docx]
"""
import copy
import re
import sys
from pathlib import Path

import docx
from docx.oxml import parse_xml
from docx.oxml.ns import nsdecls, qn
from docx.shared import Cm, Pt

ICI = Path(__file__).resolve().parent
GABARIT = ICI / "gabarit-phase2.docx"
SOURCE = ICI / "rapport.md"
SORTIE_DEFAUT = ICI.parents[2] / "CanDroneX_Rapport_P2_Microservices.docx"

LARGEUR_TEXTE = 9840          # largeur utile de la page, en twips (comme les tableaux du gabarit)
LARGEUR_IMAGE = Cm(16.5)
BLEU_TITRE = "2C3E5E"
GRIS_CONSIGNE = "666666"

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def normaliser(texte):
    return re.sub(r"\s+", " ", texte.replace("’", "'").replace(" ", " ")).strip()


def echapper(texte):
    return texte.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# --- Lecture de la source -------------------------------------------------------------------------

def lire_blocs():
    blocs = []
    for ligne in SOURCE.read_text(encoding="utf-8").splitlines():
        entete = re.match(r"^@@([!-]?)\s+(.*\S)\s*$", ligne)
        if entete:
            blocs.append({"mode": entete.group(1), "titre": entete.group(2), "lignes": []})
        elif blocs:
            blocs[-1]["lignes"].append(ligne)
    return blocs


# --- Production des éléments Word -----------------------------------------------------------------

class Redacteur:
    def __init__(self, document):
        self.doc = document
        self.figures = 0
        self.num_decimal = None

    # Texte en ligne : **gras**, *italique*, `code`
    def runs(self, texte, gras=False, couleur=None, taille=None):
        xml = []
        for morceau in re.split(r"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)", texte):
            if not morceau:
                continue
            g, i, code = gras, False, False
            if morceau.startswith("**"):
                morceau, g = morceau[2:-2], True
            elif morceau.startswith("`"):
                morceau, code = morceau[1:-1], True
            elif morceau.startswith("*") and len(morceau) > 2:
                morceau, i = morceau[1:-1], True
            proprietes = ""
            if code:
                proprietes += '<w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:cs="Consolas"/>'
            if g:
                proprietes += "<w:b/><w:bCs/>"
            if i:
                proprietes += "<w:i/><w:iCs/>"
            if couleur:
                proprietes += f'<w:color w:val="{couleur}"/>'
            if code or taille:
                demi_points = int((taille or 9.5) * 2)
                proprietes += f'<w:sz w:val="{demi_points}"/><w:szCs w:val="{demi_points}"/>'
            xml.append(f'<w:r><w:rPr>{proprietes}</w:rPr><w:t xml:space="preserve">{echapper(morceau)}</w:t></w:r>')
        return "".join(xml)

    def paragraphe(self, texte, style=None, apres=120, avant=0, gras=False, couleur=None, taille=None,
                   alignement=None, numerotation=None, retrait=None):
        proprietes = ""
        if style:
            proprietes += f'<w:pStyle w:val="{style}"/>'
        if numerotation:
            numero, niveau = numerotation
            proprietes += f'<w:numPr><w:ilvl w:val="{niveau}"/><w:numId w:val="{numero}"/></w:numPr>'
        proprietes += f'<w:spacing w:before="{avant}" w:after="{apres}"/>'
        if retrait:
            proprietes += f'<w:ind w:left="{retrait}"/>'
        if alignement:
            proprietes += f'<w:jc w:val="{alignement}"/>'
        return parse_xml(f'<w:p {nsdecls("w")}><w:pPr>{proprietes}</w:pPr>'
                         f'{self.runs(texte, gras, couleur, taille)}</w:p>')

    def titre(self, texte, niveau):
        if niveau == 2:
            return self.paragraphe(texte, "Heading2", avant=200, apres=100, gras=True, couleur=BLEU_TITRE)
        return self.paragraphe(texte, "Heading3", avant=160, apres=80, gras=True, couleur=BLEU_TITRE, taille=11.5)

    def code(self, lignes):
        elements = []
        for k, ligne in enumerate(lignes):
            apres = 120 if k == len(lignes) - 1 else 0
            elements.append(parse_xml(
                f'<w:p {nsdecls("w")}><w:pPr><w:shd w:val="clear" w:color="auto" w:fill="F2F4F7"/>'
                f'<w:spacing w:before="0" w:after="{apres}" w:line="240" w:lineRule="auto"/><w:ind w:left="120"/></w:pPr>'
                f'<w:r><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:cs="Consolas"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr>'
                f'<w:t xml:space="preserve">{echapper(ligne) or " "}</w:t></w:r></w:p>'))
        return elements

    def tableau(self, lignes):
        rangees = [[c.strip() for c in l.strip().strip("|").split("|")] for l in lignes]
        separateur = next((k for k, r in enumerate(rangees) if all(re.fullmatch(r":?-{3,}:?(\d+)?", c) for c in r)), None)
        poids = None
        if separateur is not None:
            poids = [int(m.group(1)) if (m := re.search(r"(\d+)$", c)) else None for c in rangees[separateur]]
            rangees.pop(separateur)
        colonnes = max(len(r) for r in rangees)
        rangees = [r + [""] * (colonnes - len(r)) for r in rangees]
        if not poids or None in poids:
            poids = [max(8, min(60, max(len(r[k]) for r in rangees))) for k in range(colonnes)]
        largeurs = [int(LARGEUR_TEXTE * p / sum(poids)) for p in poids]
        largeurs[-1] += LARGEUR_TEXTE - sum(largeurs)

        bordure = '<w:{0} w:val="single" w:sz="4" w:space="0" w:color="BFC7D4"/>'
        xml = [f'<w:tbl {nsdecls("w")}><w:tblPr><w:tblW w:w="{LARGEUR_TEXTE}" w:type="dxa"/><w:tblBorders>',
               "".join(bordure.format(c) for c in ("top", "left", "bottom", "right", "insideH", "insideV")),
               '</w:tblBorders><w:tblLayout w:type="fixed"/><w:tblCellMar><w:left w:w="10" w:type="dxa"/><w:right w:w="10" w:type="dxa"/></w:tblCellMar>'
               '<w:tblLook w:val="04A0" w:firstRow="1" w:lastRow="0" w:firstColumn="1" w:lastColumn="0" w:noHBand="0" w:noVBand="1"/></w:tblPr><w:tblGrid>',
               "".join(f'<w:gridCol w:w="{l}"/>' for l in largeurs), "</w:tblGrid>"]
        for k, rangee in enumerate(rangees):
            entete = k == 0
            xml.append(f'<w:tr><w:trPr>{"<w:tblHeader/>" if entete else "<w:cantSplit/>"}</w:trPr>')
            for largeur, cellule in zip(largeurs, rangee):
                fond = '<w:shd w:val="clear" w:color="auto" w:fill="2C3E5E"/>' if entete else ""
                paragraphes = "".join(
                    f'<w:p><w:pPr><w:spacing w:before="0" w:after="40"/></w:pPr>'
                    f'{self.runs(morceau, gras=entete, couleur="FFFFFF" if entete else None, taille=9.5)}</w:p>'
                    for morceau in cellule.split("<br>"))
                xml.append(f'<w:tc><w:tcPr><w:tcW w:w="{largeur}" w:type="dxa"/>{fond}<w:tcMar><w:top w:w="60" w:type="dxa"/>'
                           f'<w:left w:w="100" w:type="dxa"/><w:bottom w:w="60" w:type="dxa"/><w:right w:w="100" w:type="dxa"/>'
                           f'</w:tcMar></w:tcPr>{paragraphes}</w:tc>')
            xml.append("</w:tr>")
        xml.append("</w:tbl>")
        return [parse_xml("".join(xml)), self.paragraphe("", apres=60)]

    def image(self, legende, chemin):
        self.figures += 1
        paragraphe = self.doc.add_paragraph()
        paragraphe.alignment = 1
        paragraphe.paragraph_format.keep_with_next = True
        paragraphe.add_run().add_picture(str((ICI / chemin).resolve()), width=LARGEUR_IMAGE)
        element = paragraphe._p
        element.getparent().remove(element)
        return [element, self.paragraphe(f"Figure {self.figures} — {legende}", apres=160, couleur="595959",
                                         taille=9.5, alignement="center")]

    def sommaire(self):
        return [parse_xml(
            f'<w:p {nsdecls("w")}><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r>'
            '<w:r><w:instrText xml:space="preserve"> TOC \\o "1-2" \\h \\z \\u </w:instrText></w:r>'
            '<w:r><w:fldChar w:fldCharType="separate"/></w:r>'
            '<w:r><w:t>Clic droit, puis « Mettre à jour les champs » pour générer le sommaire.</w:t></w:r>'
            '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')]

    def liste_numerotee(self):
        """Nouvelle liste décimale, qui repart à 1."""
        numerotation = self.doc.part.numbering_part.element
        if self.num_decimal is None:
            identifiants = [int(a.get(qn("w:abstractNumId"))) for a in numerotation.findall(qn("w:abstractNum"))]
            self.num_decimal = max(identifiants) + 1
            abstrait = parse_xml(
                f'<w:abstractNum {nsdecls("w")} w:abstractNumId="{self.num_decimal}"><w:multiLevelType w:val="hybridMultilevel"/>'
                '<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/>'
                '<w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>')
            premier_num = numerotation.find(qn("w:num"))
            premier_num.addprevious(abstrait)
        numeros = [int(n.get(qn("w:numId"))) for n in numerotation.findall(qn("w:num"))]
        nouveau = max(numeros) + 1
        numerotation.append(parse_xml(
            f'<w:num {nsdecls("w")} w:numId="{nouveau}"><w:abstractNumId w:val="{self.num_decimal}"/>'
            '<w:lvlOverride w:ilvl="0"><w:startOverride w:val="1"/></w:lvlOverride></w:num>'))
        return nouveau

    def rendre(self, lignes):
        elements, k = [], 0
        paragraphe_courant = []

        def vider():
            if paragraphe_courant:
                elements.append(self.paragraphe(" ".join(paragraphe_courant)))
                paragraphe_courant.clear()

        liste_numero = None
        while k < len(lignes):
            ligne = lignes[k]
            brut = ligne.strip()
            if not brut:
                vider(); liste_numero = None; k += 1; continue
            if brut.startswith("```"):
                vider(); fin = k + 1
                while fin < len(lignes) and not lignes[fin].strip().startswith("```"):
                    fin += 1
                elements += self.code(lignes[k + 1:fin]); k = fin + 1; continue
            if brut == "[[SOMMAIRE]]":
                vider(); elements += self.sommaire(); k += 1; continue
            if brut.startswith("|"):
                vider(); fin = k
                while fin < len(lignes) and lignes[fin].strip().startswith("|"):
                    fin += 1
                elements += self.tableau(lignes[k:fin]); k = fin; continue
            image = re.fullmatch(r"!\[(.*)\]\((.+)\)", brut)
            if image:
                vider(); elements += self.image(image.group(1), image.group(2)); k += 1; continue
            if brut.startswith("### "):
                vider(); elements.append(self.titre(brut[4:], 3)); k += 1; continue
            if brut.startswith("## "):
                vider(); elements.append(self.titre(brut[3:], 2)); k += 1; continue
            puce = re.match(r"^(\s*)- (.*)$", ligne)
            numero = re.match(r"^(\s*)\d+\. (.*)$", ligne)
            if puce or numero:
                vider()
                retrait, texte = (puce or numero).groups()
                niveau = 1 if len(retrait) >= 2 else 0
                # suite de l'élément sur les lignes suivantes, indentées
                while k + 1 < len(lignes) and re.match(r"^\s{2,}\S", lignes[k + 1]) and not re.match(r"^\s*(- |\d+\. )", lignes[k + 1]):
                    k += 1; texte += " " + lignes[k].strip()
                if numero:
                    liste_numero = liste_numero or self.liste_numerotee()
                    elements.append(self.paragraphe(texte, "ListParagraph", apres=60, numerotation=(liste_numero, niveau)))
                else:
                    elements.append(self.paragraphe(texte, "ListParagraph", apres=60, numerotation=(2, niveau)))
                k += 1; continue
            if brut.startswith("> "):
                vider(); elements.append(self.paragraphe(brut[2:], couleur="44546A", retrait=240)); k += 1; continue
            paragraphe_courant.append(brut); k += 1
        vider()
        return elements


# --- Application au gabarit -----------------------------------------------------------------------

def est_titre(element):
    style = element.find(f"{{{W}}}pPr/{{{W}}}pStyle")
    return element.tag == f"{{{W}}}p" and style is not None and style.get(qn("w:val")).startswith("Heading")


def niveau(element):
    return int(element.find(f"{{{W}}}pPr/{{{W}}}pStyle").get(qn("w:val"))[-1])


def texte(element):
    return "".join(t.text or "" for t in element.iter(f"{{{W}}}t"))


def remplir_page_de_titre(document, lignes):
    valeurs = dict(l.split(":", 1) for l in lignes if ":" in l)
    valeurs = {normaliser(k): v.strip() for k, v in valeurs.items()}
    for rangee in document.tables[0].rows:
        cle = normaliser(rangee.cells[0].text)
        if cle in valeurs:
            cellule = rangee.cells[1]
            paragraphe = cellule.paragraphs[0]
            for run in paragraphe.runs[1:]:
                run._r.getparent().remove(run._r)
            run = paragraphe.runs[0]
            run.text = valeurs[cle]
            run.italic = False
            run.font.color.rgb = None


def appliquer(document, blocs, redacteur):
    corps = document.element.body
    for bloc in blocs:
        if bloc["titre"].lower() == "page de titre":
            remplir_page_de_titre(document, bloc["lignes"]); continue
        cible = next((e for e in corps if est_titre(e) and normaliser(texte(e)) == normaliser(bloc["titre"])), None)
        if cible is None:
            raise SystemExit(f"Titre introuvable dans le gabarit : {bloc['titre']}")
        # Éléments du bloc : jusqu'au titre suivant (de même niveau ou supérieur avec « ! »)
        a_retirer, suivant = [], cible.getnext()
        while suivant is not None and suivant.tag != f"{{{W}}}sectPr":
            if est_titre(suivant) and (bloc["mode"] != "!" or niveau(suivant) <= niveau(cible)):
                break
            a_retirer.append(suivant); suivant = suivant.getnext()
        for element in a_retirer:
            corps.remove(element)
        if bloc["mode"] == "-":
            corps.remove(cible); continue
        ancre = cible
        for element in redacteur.rendre(bloc["lignes"]):
            ancre.addnext(element); ancre = element


def sections_a_rediger(document):
    restantes, titre_courant = [], None
    for element in document.element.body:
        if est_titre(element):
            titre_courant = texte(element)
        elif element.tag == f"{{{W}}}p" and any(c.get(qn("w:val")) == GRIS_CONSIGNE for c in element.iter(f"{{{W}}}color")) \
                and titre_courant and titre_courant not in restantes and texte(element).strip():
            restantes.append(titre_courant)
    return restantes


def main():
    sortie = Path(sys.argv[1]) if len(sys.argv) > 1 else SORTIE_DEFAUT
    document = docx.Document(str(GABARIT))
    redacteur = Redacteur(document)
    appliquer(document, lire_blocs(), redacteur)
    document.save(str(sortie))
    restantes = sections_a_rediger(document)
    print(f"Rapport écrit : {sortie} ({redacteur.figures} figure(s))")
    print(f"Sections encore à rédiger ({len(restantes)}) : " + ("; ".join(restantes) if restantes else "aucune"))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
