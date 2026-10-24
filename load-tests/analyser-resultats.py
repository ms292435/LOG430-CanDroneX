"""Analyse d'une exécution de load-tests/executer-mesure.sh, produite dans <dossier>/analyse.md.

Usage : python3 load-tests/analyser-resultats.py load-tests/resultats/<exécution>

Critères de l'énoncé (Phase 2) :
  - débit tenu : aucune itération abandonnée, taux d'erreur < 1 %, file d'activation sans croissance continue ;
  - délai d'activation : P95 de activatedAt - acceptedAt <= 5 s (services non actifs comptés au-delà de 5 s).
"""
import csv
import json
import re
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path


def lire_horodatage(texte):
    return datetime.fromisoformat(texte.replace("Z", "+00:00"))


def pente_par_minute(points):
    """Régression linéaire (moindres carrés) : variation de la file, en demandes par minute."""
    if len(points) < 3:
        return float("nan")
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    variance = sum((x - mx) ** 2 for x in xs)
    return 60 * sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / variance if variance else 0.0


def main(dossier):
    dossier = Path(dossier)
    resume = json.loads((dossier / "resume-k6.json").read_text(encoding="utf-8"))
    fenetre = json.loads((dossier / "fenetre.json").read_text(encoding="utf-8"))
    metriques = resume["metrics"]
    debut_plateau, fin = lire_horodatage(fenetre["debutPlateau"]), lire_horodatage(fenetre["finTest"])
    duree_plateau = (fin - debut_plateau).total_seconds()

    # Débit et erreurs du plateau (sous-métriques étiquetées phase:plateau)
    echecs = metriques["http_req_failed{phase:plateau}"]["values"]
    requetes_plateau = echecs["passes"] + echecs["fails"]
    debit_plateau = requetes_plateau / duree_plateau
    taux_erreur = echecs["rate"]
    abandonnees = metriques.get("dropped_iterations", {}).get("values", {}).get("count", 0)
    duree = metriques["http_req_duration"]["values"]

    # File d'activation : demandes reçues ou en cours, pendant le plateau
    points = []
    with open(dossier / "file-activation.csv", encoding="utf-8") as f:
        for ligne in csv.DictReader(f):
            if not ligne["recues"]:
                continue
            instant = lire_horodatage(ligne["horodatage"])
            if debut_plateau <= instant <= fin:
                points.append(((instant - debut_plateau).total_seconds(), int(ligne["recues"]) + int(ligne["en_cours"])))
    file_valeurs = [p[1] for p in points]
    pente = pente_par_minute(points)
    moyenne_file = sum(file_valeurs) / len(file_valeurs) if file_valeurs else float("nan")
    # « Croissance continue » : une pente qui ferait grossir la file de plus de 10 % de sa taille moyenne par minute.
    file_stable = bool(file_valeurs) and pente <= 0.10 * moyenne_file

    # Processeur par conteneur
    cpu = defaultdict(list)
    chemin_ressources = dossier / "ressources.csv"
    if chemin_ressources.exists():
        for ligne in csv.reader(open(chemin_ressources, encoding="utf-8")):
            if len(ligne) >= 3 and ligne[2].endswith("%"):
                cpu[re.sub(r"^candronex-|-1$", "", ligne[1])].append(float(ligne[2][:-1]))

    # P95 (sortie de psql -x)
    p95 = {}
    section = None
    for ligne in (dossier / "p95-delai-activation.txt").read_text(encoding="utf-8").splitlines():
        if ligne.startswith("== Plateau"):
            section = "plateau"
        elif ligne.startswith("== Test complet"):
            section = "complet"
        elif "|" in ligne and section:
            cle, valeur = (x.strip() for x in ligne.split("|", 1))
            p95.setdefault(section, {})[cle] = valeur

    debit_tenu = abandonnees == 0 and taux_erreur < 0.01 and file_stable
    p95_plateau = p95.get("plateau", {})
    lignes = [
        f"# Analyse de l'exécution {dossier.name}",
        "",
        f"Protocole : montée {fenetre['montee']}, plateau {fenetre['plateau']} à {fenetre['tauxCible']} op/s "
        f"(60 % consultations, 30 % commandes, 10 % enregistrements). Plateau du {fenetre['debutPlateau']} au {fenetre['finTest']}.",
        "",
        "## Résultats par rapport aux cibles",
        "",
        "| Mesure | Cible | Résultat | Atteinte |",
        "|---|---|---|---|",
        f"| Débit soutenu (plateau) | ≥ {fenetre['tauxCible']} op/s | {debit_plateau:.1f} op/s ({int(requetes_plateau)} requêtes en {duree_plateau:.0f} s) | {'oui' if debit_plateau >= 0.99 * fenetre['tauxCible'] else 'non'} |",
        f"| Itérations abandonnées | 0 | {int(abandonnees)} | {'oui' if abandonnees == 0 else 'non'} |",
        f"| Taux d'erreur (plateau) | < 1 % | {100 * taux_erreur:.3f} % | {'oui' if taux_erreur < 0.01 else 'non'} |",
        f"| File d'activation (plateau) | pas de croissance continue | moyenne {moyenne_file:.0f}, max {max(file_valeurs, default=0)}, tendance {pente:+.1f} demandes/min | {'oui' if file_stable else 'non'} |",
        f"| Délai d'activation P95 (plateau) | ≤ 5 s | {p95_plateau.get('p95_s', '?')} s sur {p95_plateau.get('services_commandes', '?')} services ({p95_plateau.get('services_non_termines', '?')} non terminés) | {'oui' if p95_plateau.get('cible_p95_atteinte') == 't' else 'non'} |",
        "",
        f"**Débit tenu : {'oui' if debit_tenu else 'non'}.**",
        "",
        "## Délai d'activation (activatedAt − acceptedAt)",
        "",
        "| Fenêtre | Services | Actifs | En échec | Non terminés | P50 (s) | P95 (s) | P99 (s) | Sous 5 s |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    for nom, cle in (("Plateau", "plateau"), ("Test complet", "complet")):
        v = p95.get(cle, {})
        lignes.append(f"| {nom} | {v.get('services_commandes')} | {v.get('services_actifs')} | {v.get('services_en_echec')} | "
                      f"{v.get('services_non_termines')} | {v.get('p50_s')} | {v.get('p95_s')} | {v.get('p99_s')} | {v.get('pct_sous_5s')} % |")
    lignes += [
        "",
        "## Latence HTTP vue par k6 (toutes opérations, montée comprise)",
        "",
        f"Moyenne {duree['avg']:.1f} ms, médiane {duree['med']:.1f} ms, P90 {duree['p(90)']:.1f} ms, "
        f"P95 {duree['p(95)']:.1f} ms, P99 {duree['p(99)']:.1f} ms, max {duree['max']:.1f} ms.",
        "",
        "## Processeur par conteneur (docker stats, toutes les 15 s ; 100 % = un cœur)",
        "",
        "| Conteneur | Moyenne | Maximum |",
        "|---|---|---|",
    ]
    for nom, valeurs in sorted(cpu.items(), key=lambda x: -sum(x[1]) / len(x[1])):
        if max(valeurs) > 0:
            lignes.append(f"| {nom} | {sum(valeurs) / len(valeurs):.0f} % | {max(valeurs):.0f} % |")
    lignes.append("")
    (dossier / "analyse.md").write_text("\n".join(lignes), encoding="utf-8")
    print("\n".join(lignes))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")   # console Windows : sortie en UTF-8 (≥, accents)
    main(sys.argv[1])
