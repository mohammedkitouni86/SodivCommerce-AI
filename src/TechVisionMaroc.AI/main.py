"""
SODIV Bureau – Service IA (FastAPI + Scikit-learn)
Recommandations produits, analyse de sentiments, tendances
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import List
import numpy as np
import pyodbc
import os
from dotenv import load_dotenv

load_dotenv()

app = FastAPI(
    title="SODIV Bureau IA API",
    description="Service d'intelligence artificielle pour recommandations et analyse de sentiments",
    version="1.0.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5000", "https://techvisionmaroc.ma"],
    allow_methods=["*"],
    allow_headers=["*"],
)

DB_CONN = os.getenv("DB_CONNECTION", "DRIVER={ODBC Driver 17 for SQL Server};SERVER=localhost;DATABASE=TechVisionMarocDB;Trusted_Connection=yes")


def get_db():
    return pyodbc.connect(DB_CONN)


# ── Recommandations ──────────────────────────────────────────────────────────

def _charger_matrice_co_achat():
    """Charge la matrice de co-achat des produits depuis la base."""
    try:
        conn = get_db()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT lc1.ProduitId AS p1, lc2.ProduitId AS p2, COUNT(*) AS poids
            FROM LignesCommande lc1
            JOIN LignesCommande lc2 ON lc1.CommandeId = lc2.CommandeId AND lc1.ProduitId < lc2.ProduitId
            GROUP BY lc1.ProduitId, lc2.ProduitId
        """)
        rows = cursor.fetchall()
        conn.close()
        return {(r.p1, r.p2): r.poids for r in rows}
    except Exception:
        return {}


@app.get("/api/recommandations/{produit_id}", response_model=dict)
async def recommandations(produit_id: int, nombre: int = 4):
    """Retourne les IDs de produits recommandés basés sur le co-achat."""
    try:
        matrice = _charger_matrice_co_achat()
        scores: dict[int, int] = {}
        for (p1, p2), poids in matrice.items():
            if p1 == produit_id:
                scores[p2] = scores.get(p2, 0) + poids
            elif p2 == produit_id:
                scores[p1] = scores.get(p1, 0) + poids

        if not scores:
            # Fallback : même catégorie
            conn = get_db()
            cursor = conn.cursor()
            cursor.execute("""
                SELECT TOP (?) p.Id FROM Produits p
                JOIN Produits ref ON ref.Id = ? AND ref.CategorieId = p.CategorieId
                WHERE p.Id != ? AND p.EstActif = 1
                ORDER BY p.NombreVentes DESC
            """, nombre, produit_id, produit_id)
            ids = [r.Id for r in cursor.fetchall()]
            conn.close()
            return {"ids": ids}

        ids = sorted(scores, key=scores.get, reverse=True)[:nombre]
        return {"ids": ids}

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


# ── Analyse de sentiments ─────────────────────────────────────────────────────

class SentimentRequest(BaseModel):
    texte: str


MOTS_POSITIFS = {"excellent", "parfait", "super", "génial", "recommande", "satisfait", "rapide", "qualité", "bravo", "top"}
MOTS_NEGATIFS = {"mauvais", "nul", "décevant", "lent", "cassé", "arnaque", "déçu", "problème", "défectueux", "retard"}


def _analyser_sentiment(texte: str) -> tuple[str, float]:
    mots = set(texte.lower().split())
    pos = len(mots & MOTS_POSITIFS)
    neg = len(mots & MOTS_NEGATIFS)
    if pos > neg:
        score = min(1.0, 0.5 + pos * 0.1)
        return "Positif", score
    elif neg > pos:
        score = max(0.0, 0.5 - neg * 0.1)
        return "Negatif", score
    return "Neutre", 0.5


@app.post("/api/sentiment", response_model=dict)
async def analyser_sentiment(req: SentimentRequest):
    if not req.texte or len(req.texte) < 3:
        raise HTTPException(status_code=400, detail="Texte trop court")
    sentiment, score = _analyser_sentiment(req.texte)
    return {"sentiment": sentiment, "score": score}


# ── Tendances ──────────────────────────────────────────────────────────────────

@app.get("/api/tendances", response_model=dict)
async def tendances(nombre: int = 6):
    """Retourne les IDs des produits tendances (ventes récentes pondérées)."""
    try:
        conn = get_db()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT TOP (?) lc.ProduitId,
                SUM(lc.Quantite * EXP(-0.1 * DATEDIFF(day, c.DateCommande, GETDATE()))) AS score
            FROM LignesCommande lc
            JOIN Commandes c ON c.Id = lc.CommandeId
            WHERE c.DateCommande >= DATEADD(day, -30, GETDATE())
              AND c.Statut NOT IN (5, 6)
            GROUP BY lc.ProduitId
            ORDER BY score DESC
        """, nombre)
        ids = [r.ProduitId for r in cursor.fetchall()]
        conn.close()
        return {"ids": ids}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


# ── Health check ──────────────────────────────────────────────────────────────

@app.get("/health")
async def health():
    return {"status": "ok", "service": "SODIV Bureau IA"}


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000, reload=True)
