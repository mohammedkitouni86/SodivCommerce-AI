using System.Globalization;

namespace TechVisionMaroc.Services;

// Traductions inline — pas besoin de fichiers .resx.
// Utilisation dans les vues : @T.Get("Accueil") ou @T.G("Accueil")
public static class T
{
    private static readonly Dictionary<string, Dictionary<string, string>> _t = new()
    {
        // ── Navbar / Topbar ──────────────────────────────
        ["Accueil"]              = new() { ["fr"]="Accueil",                  ["ar"]="الرئيسية",                 ["en"]="Home" },
        ["Produits"]             = new() { ["fr"]="Produits",                 ["ar"]="المنتجات",                ["en"]="Products" },
        ["Contact"]              = new() { ["fr"]="Contact",                  ["ar"]="اتصل بنا",                 ["en"]="Contact" },
        ["Connexion"]            = new() { ["fr"]="Connexion",                ["ar"]="تسجيل الدخول",            ["en"]="Sign in" },
        ["MonProfil"]            = new() { ["fr"]="Mon profil",               ["ar"]="ملفي الشخصي",             ["en"]="My profile" },
        ["Deconnexion"]          = new() { ["fr"]="Déconnexion",              ["ar"]="تسجيل الخروج",            ["en"]="Sign out" },
        ["Admin"]                = new() { ["fr"]="Admin",                    ["ar"]="المسؤول",                 ["en"]="Admin" },
        ["SuperAdmin"]           = new() { ["fr"]="Super Admin",              ["ar"]="المسؤول الأعلى",          ["en"]="Super Admin" },
        ["Rechercher"]           = new() { ["fr"]="Rechercher un produit, une marque...", ["ar"]="ابحث عن منتج أو علامة تجارية...", ["en"]="Search a product, a brand..." },
        ["Catalogue"]            = new() { ["fr"]="Catalogue",                ["ar"]="الكتالوج",                ["en"]="Catalog" },
        ["APropos"]              = new() { ["fr"]="À propos",                 ["ar"]="عن الشركة",               ["en"]="About" },

        // ── Hero ─────────────────────────────────────────
        ["HeroBadge"]            = new() { ["fr"]="Nouveau : Livraison GRATUITE dès 1 000 MAD", ["ar"]="جديد: شحن مجاني ابتداءً من 1000 درهم", ["en"]="New: FREE shipping from 1,000 MAD" },
        ["HeroTitre1"]           = new() { ["fr"]="Votre partenaire technologique au", ["ar"]="شريككم التكنولوجي في", ["en"]="Your technology partner in" },
        ["Maroc"]                = new() { ["fr"]="Maroc",                    ["ar"]="المغرب",                  ["en"]="Morocco" },
        ["HeroSous"]             = new() { ["fr"]="Matériel informatique, fournitures de bureau et solutions technologiques. Qualité premium, prix compétitifs, livraison rapide partout au Maroc.", ["ar"]="معدات معلوماتية، لوازم مكتبية وحلول تكنولوجية. جودة عالية، أسعار تنافسية، توصيل سريع في جميع أنحاء المغرب.", ["en"]="IT hardware, office supplies and technology solutions. Premium quality, competitive prices, fast delivery across Morocco." },
        ["DecouvrirCatalogue"]   = new() { ["fr"]="Découvrir le catalogue",   ["ar"]="اكتشف الكتالوج",          ["en"]="Browse catalog" },
        ["DemanderDevis"]        = new() { ["fr"]="Demander un devis",        ["ar"]="طلب عرض سعر",             ["en"]="Request a quote" },
        ["NbProduits"]           = new() { ["fr"]="Produits",                 ["ar"]="منتجات",                  ["en"]="Products" },
        ["NbClients"]            = new() { ["fr"]="Clients",                  ["ar"]="عملاء",                   ["en"]="Customers" },
        ["NbCommandes"]          = new() { ["fr"]="Commandes",                ["ar"]="طلبات",                   ["en"]="Orders" },
        ["AnsExperience"]        = new() { ["fr"]="d'expérience",             ["ar"]="من الخبرة",               ["en"]="of experience" },

        // ── Avantages ────────────────────────────────────
        ["LivraisonGratuite"]    = new() { ["fr"]="Livraison gratuite",       ["ar"]="شحن مجاني",               ["en"]="Free delivery" },
        ["Des500"]               = new() { ["fr"]="Dès 1 000 MAD",            ["ar"]="ابتداءً من 1000 درهم",    ["en"]="From 1,000 MAD" },
        ["PaiementSecurise"]     = new() { ["fr"]="Paiement sécurisé",        ["ar"]="دفع آمن",                 ["en"]="Secure payment" },
        ["SSL256"]               = new() { ["fr"]="SSL 256 bits",             ["ar"]="SSL 256 بت",              ["en"]="256-bit SSL" },
        ["Retour14"]             = new() { ["fr"]="Retour 14 jours",          ["ar"]="إرجاع خلال 14 يوماً",    ["en"]="14-day return" },
        ["SatisfaitRembourse"]   = new() { ["fr"]="Satisfait ou remboursé",   ["ar"]="مرضي أو نسترد المال",   ["en"]="Satisfied or refunded" },
        ["Support77"]            = new() { ["fr"]="Support 7j/7",             ["ar"]="دعم 7 أيام/7",           ["en"]="7-day support" },

        // ── Catégories ───────────────────────────────────
        ["NosCategories"]        = new() { ["fr"]="Nos Catégories",           ["ar"]="فئاتنا",                  ["en"]="Our Categories" },
        ["CliquezCategorie"]     = new() { ["fr"]="Cliquez sur une catégorie pour voir les sous-catégories", ["ar"]="انقر على فئة لعرض الفئات الفرعية", ["en"]="Click a category to see sub-categories" },

        // ── Produits ─────────────────────────────────────
        ["ProduitsVedettes"]     = new() { ["fr"]="Produits Vedettes",        ["ar"]="منتجات مميزة",            ["en"]="Featured Products" },
        ["MeilleursSelections"]  = new() { ["fr"]="Nos meilleures sélections pour vous", ["ar"]="أفضل اختياراتنا لك", ["en"]="Our best picks for you" },
        ["VoirTout"]             = new() { ["fr"]="Voir tout",                ["ar"]="عرض الكل",                ["en"]="View all" },
        ["Tendances"]            = new() { ["fr"]="Tendances du moment",      ["ar"]="رائج الآن",               ["en"]="Trending now" },
        ["PlusPopulaires"]       = new() { ["fr"]="Les produits les plus populaires", ["ar"]="أكثر المنتجات شعبية", ["en"]="The most popular products" },
        ["VoirPlus"]             = new() { ["fr"]="Voir plus",                ["ar"]="عرض المزيد",              ["en"]="See more" },
        ["RecommandePourVous"]   = new() { ["fr"]="Recommandé pour vous",     ["ar"]="موصى به لك",              ["en"]="Recommended for you" },
       

        // ── Banner IA ────────────────────────────────────
        ["RecoIA"]               = new() { ["fr"]="Recommandations Intelligentes", ["ar"]="توصيات ذكية",        ["en"]="Smart Recommendations" },
        ["RecoIASous"]           = new() { ["fr"]="Notre IA analyse vos préférences pour vous proposer les produits parfaits", ["ar"]="يحلل الذكاء الاصطناعي تفضيلاتك ليقترح عليك المنتجات المثالية", ["en"]="Our AI analyzes your preferences to offer you the perfect products" },
        ["DecouvrirReco"]        = new() { ["fr"]="Découvrir mes recommandations", ["ar"]="اكتشف توصياتي",      ["en"]="Discover my recommendations" },

        // ── Footer ───────────────────────────────────────
        ["FooterSlogan"]         = new() { ["fr"]="Votre partenaire technologique au Maroc. Matériel informatique et fournitures de bureau de qualité.", ["ar"]="شريككم التكنولوجي في المغرب. معدات معلوماتية ولوازم مكتبية عالية الجودة.", ["en"]="Your technology partner in Morocco. Quality IT hardware and office supplies." },
        ["Navigation"]           = new() { ["fr"]="Navigation",               ["ar"]="التنقل",                  ["en"]="Navigation" },
        ["TousDroits"]           = new() { ["fr"]="Tous droits réservés",     ["ar"]="جميع الحقوق محفوظة",     ["en"]="All rights reserved" },
        ["PolitiqueConf"]        = new() { ["fr"]="Politique de confidentialité", ["ar"]="سياسة الخصوصية",     ["en"]="Privacy policy" },
        ["CGV"]                  = new() { ["fr"]="CGV",                      ["ar"]="الشروط العامة",           ["en"]="Terms of sale" },
        ["Paiement100Securise"]  = new() { ["fr"]="Paiement 100% sécurisé",   ["ar"]="دفع آمن 100%",           ["en"]="100% secure payment" },
        ["Horaires"]             = new() { ["fr"]="Lun–Sam : 9h–18h",         ["ar"]="الإثنين-السبت: 9ص-6م",   ["en"]="Mon–Sat: 9am–6pm" }
    };

    public static string Get(string cle)
    {
        var langue = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (_t.TryGetValue(cle, out var trad))
        {
            if (trad.TryGetValue(langue, out var s)) return s;
            if (trad.TryGetValue("fr", out var fr)) return fr;
        }
        return cle;
    }

    public static string G(string cle) => Get(cle);
}
