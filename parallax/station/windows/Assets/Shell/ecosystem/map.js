(function () {
        const NS = 'http://www.w3.org/2000/svg';
        const svg = document.getElementById('eco');
        const wrap = document.getElementById('eco-wrap');
        const tip = document.getElementById('eco-tip');
        if (!svg) return;

        const C = {
            plexus: '#22d3ee', slipstream: '#4ade80', positronic: '#facc15',
            commerce: '#fb7185', labs: '#c4b5fd', products: '#fb923c', neutral: '#e2e8f0',
            dim: 'rgba(255,255,255,.45)', oss: '#6ee7b7'
        };

        const GROUPS = {
            hub:        { label: 'The company',            color: '#ffffff' },
            suite:      { label: 'Core suite',             color: C.neutral },
            foundation: { label: 'Shared foundation',      color: C.neutral },
            services:   { label: 'Services & delivery',    color: C.neutral },
            commerce:   { label: 'Ventures & commerce',    color: C.commerce },
            products:   { label: 'Standalone product',     color: C.products },
            labs:       { label: 'Parallax Labs',          color: C.labs },
            pipeline:   { label: 'Internal pipeline',      color: C.dim },
            bets:       { label: 'Small bets',             color: C.dim }
        };

        const STAGE_COLORS = {
            'IN PRODUCTION': '#6ee7b7', 'OPERATING': '#6ee7b7', 'LIVE': '#6ee7b7', 'CLOSED ACCESS': '#6ee7b7',
            'IN DEVELOPMENT': '#7dd3fc', 'IN DESIGN': '#7dd3fc', 'OFFER IN DEFINITION': '#7dd3fc',
            'ALPHA · DEMOABLE': '#fcd34d', 'PROVING GROUND': '#fcd34d',
            'LABS': '#c4b5fd',
            'PLANNED': 'rgba(255,255,255,.6)', 'FOUNDATION': 'rgba(255,255,255,.7)',
            'ENTITY': 'rgba(255,255,255,.7)',
            'CONCEPT': 'rgba(255,255,255,.45)', 'PIPELINE': 'rgba(255,255,255,.45)'
        };

        // Source posture and first NEXUS demonstrations (September 2026). The badge
        // draws on the node, the chip shows in the tooltip.
        const SOURCE = {
            open:   { badge: 'OPEN SOURCE',      chip: 'OPEN SOURCE' },
            cli:    { badge: 'OPEN-SOURCE CLI',  chip: 'OPEN-SOURCE CLI · REST CLOSED' },
            core:   { badge: 'OPEN-SOURCE CORE', chip: 'CORE OPEN SOURCE · AGENCY CLOSED' },
            closed: { badge: null,               chip: 'CLOSED SOURCE' }
        };

        // A concept node renders with a dashed border.
        const NODES = [
            { id: 'parallax', name: 'PARALLAX', group: 'hub', stage: '', x: 640, y: 470, big: true, sub: ['CREATIVE COMMERCE CLOUD'], mark: '/assets/Parallax%20Mark.svg', core: {
                id: 'nexus', name: 'NEXUS', description: 'AUTH · ACCOUNTS · BILLING',
                blurb: 'The shared authentication, accounts, and billing core inside PARALLAX. Being prepared for open source; Eros, Psyche, Perpetuity, Holo, and Relay are its first planned demonstrations.'
              },
              blurb: 'Parallax is Creative Commerce Cloud: the shared platform for work, people, AI and commerce. NEXUS provides its authentication, accounts and billing core.' },

            { id: 'thetalabs', name: 'THETA LABS', group: 'services', stage: 'OPERATING', x: 640, y: 100,
              blurb: 'Theta Labs, LLC — the client services business. Client work runs on the suite and funds the platform.' },
            { id: 'wphyperdrive', name: 'WPHYPERDRIVE', group: 'services', stage: 'OFFER IN DEFINITION', x: 330, y: 100, source: 'closed',
              blurb: 'Productized WordPress/WooCommerce escape path — powered by Apollo, delivered through Theta Labs. Closed source.' },
            { id: 'petrichor', name: 'PETRICHOR', group: 'services', stage: 'IN DEVELOPMENT', x: 330, y: 175, source: 'open',
              blurb: 'Open-source WordPress theme and design system built to make Elementor obsolete — MIT core, GPL theme and plugin. Meant for the client sites Theta Labs and Web Services deliver.' },
            { id: 'webservices', name: 'WEB SERVICES', group: 'services', stage: 'OPERATING', x: 640, y: 660,
              blurb: 'The consulting & delivery arm — real client demand that funds and battle-tests everything above it.' },

            { id: 'slipstream', name: 'SLIPSTREAM', group: 'suite', color: C.slipstream, stage: 'IN PRODUCTION', x: 955, y: 248, source: 'cli',
              blurb: 'Manage the work — tasks, projects, agents, review, and delivery. Ships while you sleep; asks before it matters. The CLI is open source; the rest stays closed. Pay-once tasks — reviews, scoping, audits — are sold at slipstream.codes/tasks and delivered through Theta Labs.' },
            { id: 'plexus', name: 'PLEXUS', group: 'suite', color: C.plexus, stage: 'IN PRODUCTION', x: 990, y: 355, source: 'core', core: {
                id: 'plexuscore', name: 'PLEXUS CORE', description: 'PEOPLE · RELATIONSHIPS · NETWORKS', color: C.plexus,
                blurb: 'The open-source people and network core inside PLEXUS. The shared foundation for Parallax social sites, including Savor.social and Partition.social, and OnlyFriends and GoFundYourself through Psyche. These integrations are planned.'
              },
              blurb: 'People & netmanage the work — requests, scope, approvals, capabilities, relationships, and trust. Splits in two: Plexus Core, the open-source people and network foundation at the heart of every Parallax social site, and Plexus Agency, the closed commercial edition.' },
            { id: 'positronic', name: 'POSITRONIC', group: 'suite', color: C.positronic, stage: 'LIVE', x: 1002, y: 470, source: 'closed',
              blurb: 'Paid managed service powered by open-source Nucleus: machines, approved models, workload placement, and shared organization context for Slipstream. Grid browser automation remains available. Install Nucleus yourself at nucleus.parallax.link. NEBULA connectivity between installations is planned.' },
            { id: 'apollo', name: 'APOLLO', group: 'foundation', stage: 'FOUNDATION', x: 955, y: 562,
              blurb: 'Migration & modernization: moving legacy systems onto maintainable platforms. Powers WPHyperdrive.' },
            { id: 'lore', name: 'LORE', group: 'pipeline', stage: 'PIPELINE', x: 900, y: 627, concept: true,
              blurb: 'Internal pipeline concept — staged privately until it earns a public role.' },

            { id: 'manifest', name: 'MANIFEST', group: 'pipeline', stage: 'PIPELINE', x: 330, y: 305, concept: true,
              blurb: 'Internal pipeline concept — staged privately until it earns a public role.' },
            { id: 'talos', name: 'TALOS', group: 'pipeline', stage: 'PIPELINE', x: 298, y: 372, concept: true,
              blurb: 'Internal pipeline concept — staged privately until it earns a public role.' },
            { id: 'eros', name: 'EROS', group: 'pipeline', stage: 'PIPELINE', x: 315, y: 470, concept: true, source: 'open', nexusDemo: true, sub: ['CREATOR SAAS CORE'],
              blurb: 'The open-source core for a creator subscription SaaS. The public-facing SaaS name is undecided; Eros names the core. Eros and Psyche are complementary cores: two sides of the same coin. Selected for the first NEXUS demonstrations: shared authentication, accounts, and billing. Integration is planned.' },
            { id: 'psyche', name: 'PSYCHE', group: 'pipeline', stage: 'PIPELINE', x: 315, y: 575, concept: true, source: 'open', nexusDemo: true,
              blurb: 'The open-source shared core for OnlyFriends and GoFundYourself, built on Plexus Core. Psyche and Eros are complementary cores: two sides of the same coin. Also selected for the first NEXUS demonstrations: shared authentication, accounts, and billing. These integrations are planned.' },
            { id: 'onlyfriends', name: 'ONLYFRIENDS', group: 'bets', stage: 'CONCEPT', x: 100, y: 520, concept: true,
              blurb: 'A social-site concept built on Psyche, which depends on Plexus Core. The dependency chain is planned.' },
            { id: 'gofundyourself', name: 'GOFUNDYOURSELF', group: 'bets', stage: 'CONCEPT', x: 110, y: 615, concept: true,
              blurb: 'A social-site concept built on Psyche, which depends on Plexus Core. The dependency chain is planned.' },
            { id: 'perpetuity', name: 'PERPETUITY', group: 'pipeline', stage: 'PIPELINE', x: 325, y: 670, concept: true, source: 'open', nexusDemo: true,
              blurb: 'An open-source pipeline app selected for the first NEXUS demonstrations: shared authentication, accounts, and billing. Integration is planned.' },

            { id: 'unravel', name: 'UNRAVEL', group: 'products', stage: 'LIVE', x: 300, y: 760, source: 'closed',
              blurb: 'Don&rsquo;t just listen to music — unravel it. Takes finished recordings apart into stems, structure, and editable loops for listeners, DJs, musicians, and producers. Live at unravel.tools; closed source.' },
            { id: 'meld', name: 'MELD', group: 'products', stage: 'CLOSED ACCESS', x: 440, y: 760, source: 'closed',
              blurb: 'Likeness licensing and identity-safe image work — opt-in, project-specific, paid, no training rights by default. Renders through Positronic. Permanently closed access: every account is reviewed and invited. Closed source.' },
            { id: 'holo', name: 'HOLO', group: 'products', stage: 'CONCEPT', x: 780, y: 760, concept: true, source: 'open', nexusDemo: true,
              blurb: 'Visual media for serious creators — a library that understands the work: originals, derivatives, people, projects, clients, rights, and where each frame has been published. Open-source concept selected for the first NEXUS demonstrations. Integration is planned.' },
            { id: 'relay', name: 'RELAY', group: 'products', stage: 'CONCEPT', x: 940, y: 760, concept: true, source: 'open', nexusDemo: true,
              blurb: 'Signals and information moving through the system — ingest sources, aggregate and normalize feeds, correlate and de-duplicate events, enrich, and publish alerts, feeds, and APIs. Open-source concept selected for the first NEXUS demonstrations. Integration is planned.' },

            { id: 'levelup', name: 'LEVEL UP', group: 'bets', stage: 'CONCEPT', x: 135, y: 300, concept: true,
              sub: ['Restalk', '↳ Iridescent', '↳ Wildcard'],
              blurb: 'Level Up Entertainment, LLC — entertainment concepts in a chain: Restalk → Iridescent → Wildcard.' },
            { id: 'partition', name: 'PARTITION.SOCIAL', group: 'bets', stage: 'CONCEPT', x: 150, y: 700, concept: true,
              blurb: 'Social small-bet concept built on Plexus Core. Integration is planned.' },

            { id: 'acquire', name: 'ACQUIRE', group: 'commerce', stage: 'PLANNED', x: 1185, y: 470, concept: true, source: 'open',
              blurb: 'Planned Apache-2.0 commerce platform — the open foundation beneath the commerce ventures.' },
            { id: 'unblurred', name: 'UNBLURRED VISION', group: 'commerce', stage: 'ENTITY', x: 1185, y: 170,
              blurb: 'Unblurred Vision, LLC — the optical venture entity behind EyeCommerce; ~12 years of optical operating experience.' },
            { id: 'eyecommerce', name: 'EYECOMMERCE', group: 'commerce', stage: 'IN DESIGN', x: 1380, y: 255,
              blurb: 'Glasses compared the way Expedia compares travel — frames, lenses, insurance, and providers as one true total package.' },
            { id: 'hearthglow', name: 'HEARTHGLOW', group: 'commerce', stage: 'CONCEPT', x: 1390, y: 470, concept: true,
              blurb: 'PREPPED! commerce venture concept on the planned Acquire platform.' },
            { id: 'prepped', name: 'PREPPED', group: 'commerce', stage: 'PROVING GROUND', x: 1185, y: 890,
              blurb: 'Prepped, LLC — the meal-kit business; the culinary proving ground.' },
            { id: 'savorsocial', name: 'SAVOR.SOCIAL', group: 'commerce', stage: 'IN DESIGN', x: 1375, y: 845,
              blurb: 'Food-culture discovery — the demand side of the culinary stack. Its social foundation is Plexus Core; integration is planned.' },
            { id: 'indulge', name: 'INDULGE', group: 'commerce', stage: 'CONCEPT', x: 1390, y: 935, concept: true,
              blurb: 'Culinary concept in the Prepped orbit.' },
            { id: 'tarragon', name: 'TARRAGON', group: 'commerce', stage: 'ALPHA · DEMOABLE', x: 1185, y: 1030,
              blurb: 'The Culinary Operating System — alpha proven end to end: cart → checkout → kitchen ticket.' },

            { id: 'atlas', name: 'ATLAS', group: 'labs', stage: 'LABS', x: 805, y: 950,
              blurb: 'Preserving, organizing, and replaying history — source-backed and correction-aware.' },
            { id: 'varta', name: 'VARTA', group: 'labs', stage: 'LABS', x: 430, y: 1000,
              blurb: 'Atlas&rsquo;s first experience: a delayed, source-backed archive of drone & missile attacks on Ukraine.' },
            { id: 'fervent', name: 'FERVENT WISHES', group: 'labs', stage: 'CONCEPT', x: 805, y: 1080, concept: true,
              sub: ['Voices of the Free World', 'United We Stand'],
              blurb: 'Atlas experience concepts: Voices of the Free World · United We Stand.' },

        ];

        NODES.push(
            {"id": "nucleus", "name": "NUCLEUS", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Open-source private infrastructure control and node onboarding."},
            {"id": "nebula", "name": "NEBULA", "group": "foundation", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Connectivity between private Nucleus installations."},
            {"id": "continuum", "name": "CONTINUUM", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Executive coordination and company-level AI direction."},
            {"id": "quark", "name": "QUARK", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Financial sense and CFO intelligence within the Parallax executive suite."},
            {"id": "klaxon", "name": "KLAXON", "group": "foundation", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Security posture and CISO capability."},
            {"id": "pulsar", "name": "PULSAR", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Marketing campaigns, distribution and audience growth."},
            {"id": "helm", "name": "HELM", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Cross-product management and operations interface."},
            {"id": "station", "name": "PARALLAX STATION", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Chromium-based workspace and portal experience."},
            {"id": "shchytok", "name": "SHCHYTOK", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Independent Chromium-derived browser foundation."},
            {"id": "parity", "name": "PARITY", "group": "foundation", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Financial tooling subordinate to Quark rather than the executive CFO role."},
            {"id": "positronicgrid", "name": "POSITRONIC GRID", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Browser automation and approved workloads through the Positronic environment."},
            {"id": "cadence", "name": "CADENCE", "group": "foundation", "stage": "EXPERIMENTAL", "x": 0, "y": 0, "blurb": "Parallax research and automation workstream."},
            {"id": "contextkits", "name": "CONTEXT KITS", "group": "foundation", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Reusable, structured context for research and AI workloads."},
            {"id": "kinetic", "name": "KINETIC", "group": "foundation", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Motion and campaign production in the Pulsar/creator toolchain."},
            {"id": "tapestry", "name": "TAPESTRY", "group": "products", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Narrative and structured storytelling work."},
            {"id": "tarragonextract", "name": "TARRAGON EXTRACT", "group": "products", "stage": "OPEN-SOURCE PLAN", "x": 0, "y": 0, "blurb": "Photo, video and URL extraction into traceable structured recipes."},
            {"id": "provenance", "name": "PROVENANCE PROTOCOL", "group": "products", "stage": "OPEN-SOURCE PLAN", "x": 0, "y": 0, "blurb": "Verifiable source and transformation lineage specification."},
            {"id": "livingcookbook", "name": "LIVING COOKBOOK APP", "group": "products", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Self-hostable family recipe archive and living collection."},
            {"id": "contentcommerce", "name": "CREATOR CONTENT KITS", "group": "products", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Reusable creator content and publishing workflows."},
            {"id": "freighter", "name": "FREIGHTER", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Package registry, signing, versioning, publishing and distribution."},
            {"id": "parallaxcli", "name": "PARALLAX CLI", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Cross-product command and migration tools that connect the operator workflow to Slipstream."},
            {"id": "archer", "name": "ARCHER", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Dependency resolution, install and configuration—not a package registry."},
            {"id": "lambogento", "name": "LAMBOGENTO", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Magento-facing integration and distribution vehicle."},
            {"id": "standalone", "name": "STANDALONE", "group": "services", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Creator-owned, self-hosted instance deployment."},
            {"id": "zefram", "name": "ZEFRAM", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Laravel-based application distribution and developer foundation."},
            {"id": "zh", "name": "ZH", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Parallax runtime and command environment."},
            {"id": "zhql", "name": "ZHQL", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Declarative workflow and orchestration language."},
            {"id": "zzh", "name": "ZZH", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Secure remote runtime entry and management."},
            {"id": "slipstreamzh", "name": "SLIPSTREAM Ж", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Cross-machine deployment and workload execution."},
            {"id": "cookbook", "name": "ARCHER COOKBOOK", "group": "services", "stage": "IN DEVELOPMENT", "x": 0, "y": 0, "blurb": "Examples and recipes for repeatable installation workflows."},
            {"id": "tck", "name": "TALBOT'S COMFORT KITCHEN", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Culinary brand and private-chef experience; creator sales and preorder use case."},
            {"id": "familycookbook", "name": "TALBOT FAMILY COOKBOOK", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Family heirlooms, historical recipes, cookbook preorders and living-recipe experiences."},
            {"id": "talbottech", "name": "TALBOT TECHNOLOGIES LLC", "group": "commerce", "stage": "ENTITY", "x": 0, "y": 0, "blurb": "Independent consulting and educational services entity."},
            {"id": "frenetic", "name": "FRENETIC SOFTWARE", "group": "commerce", "stage": "ENTITY", "x": 0, "y": 0, "blurb": "Education publishing, coaching, Laravel courses and information products."},
            {"id": "tryzantha", "name": "TRYZANTHA", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Fervent Wishes imprint for defiant creative and advocacy work."},
            {"id": "myravelle", "name": "MYRAVELLE", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Fervent Wishes art imprint."},
            {"id": "singularity", "name": "SINGULARITY STUDIOS LLC", "group": "commerce", "stage": "ENTITY", "x": 0, "y": 0, "blurb": "Ownership and studio umbrella; not a product dependency."},
            {"id": "rose", "name": "ROSE ENTERPRISES", "group": "commerce", "stage": "ENTITY", "x": 0, "y": 0, "blurb": "Separate venture and operator relationship."},
            {"id": "epsilon", "name": "EPSILON PRIME", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Creative studio within Theta Labs, including cinematic music and original releases."},
            {"id": "zapovit", "name": "THE ZAPOVIT INITIATIVE", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Fervent Wishes/Tryzantha humanitarian communications project."},
            {"id": "nemtsov", "name": "OPERATION NEMTSOV", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Independent educational and advocacy campaign work."},
            {"id": "chronicles", "name": "THE LIVING CHRONICLES", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Animated historical film and public storytelling concept."},
            {"id": "talbottechchannel", "name": "TALBOT TECH", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Technology streaming and education channel."},
            {"id": "talbotartist", "name": "TAL / BOT", "group": "commerce", "stage": "PLANNED", "x": 0, "y": 0, "blurb": "Artist persona and original release pipeline."},
        );

        const SECTIONS = [
    {
        "id": "infrastructure",
        "label": "INFRASTRUCTURE",
        "x": 1110,
        "y": 80,
        "w": 360,
        "h": 690,
        "cols": 1,
        "ids": [
            "nucleus",
            "nebula",
            "positronicgrid",
            "acquire"
        ]
    },
    {
        "id": "operations",
        "label": "AI & OPERATIONS",
        "x": 30,
        "y": 780,
        "w": 710,
        "h": 650,
        "cols": 3,
        "ids": [
            "continuum",
            "quark",
            "klaxon",
            "pulsar",
            "helm",
            "station",
            "shchytok",
            "parity",
            "cadence",
            "contextkits",
            "kinetic"
        ]
    },
    {
        "id": "delivery",
        "label": "PACKAGING & DISTRIBUTION",
        "x": 760,
        "y": 780,
        "w": 710,
        "h": 650,
        "cols": 3,
        "ids": [
            "freighter",
            "archer",
            "parallaxcli",
            "zefram",
            "zh",
            "zhql",
            "zzh",
            "slipstreamzh",
            "cookbook",
            "lambogento",
            "wphyperdrive",
            "petrichor",
            "standalone"
        ]
    },
    {
        "id": "creator",
        "label": "CREATOR TOOLS & ARCHIVES",
        "x": 30,
        "y": 1460,
        "w": 710,
        "h": 410,
        "cols": 3,
        "ids": [
            "tapestry",
            "tarragonextract",
            "provenance",
            "livingcookbook",
            "contentcommerce",
            "tarragon",
            "atlas",
            "varta"
        ]
    },
    {
        "id": "prepped-company",
        "label": "PREPPED! LLC",
        "x": 760,
        "y": 1460,
        "w": 710,
        "h": 410,
        "cols": 2,
        "ids": [
            "prepped",
            "tck",
            "familycookbook",
            "savorsocial",
            "indulge",
            "hearthglow"
        ]
    },
    {
        "id": "unblurred-company",
        "label": "UNBLURRED VISION LLC",
        "x": 30,
        "y": 1900,
        "w": 710,
        "h": 280,
        "cols": 2,
        "ids": [
            "unblurred",
            "eyecommerce"
        ]
    },
    {
        "id": "theta-company",
        "label": "THETA LABS LLC",
        "x": 760,
        "y": 1900,
        "w": 710,
        "h": 280,
        "cols": 2,
        "ids": [
            "thetalabs",
            "epsilon"
        ]
    },
    {
        "id": "talbot-company",
        "label": "TALBOT TECHNOLOGIES LLC",
        "x": 30,
        "y": 2210,
        "w": 710,
        "h": 320,
        "cols": 2,
        "ids": [
            "talbottech",
            "frenetic",
            "talbottechchannel",
            "talbotartist"
        ]
    },
    {
        "id": "fervent-company",
        "label": "FERVENT WISHES CO.",
        "x": 760,
        "y": 2210,
        "w": 710,
        "h": 440,
        "cols": 2,
        "ids": [
            "fervent",
            "tryzantha",
            "myravelle",
            "zapovit",
            "nemtsov",
            "chronicles"
        ]
    },
    {
        "id": "levelup-company",
        "label": "LEVEL UP ENTERTAINMENT LLC",
        "x": 30,
        "y": 2560,
        "w": 710,
        "h": 270,
        "cols": 2,
        "ids": [
            "levelup"
        ]
    },
    {
        "id": "singularity-company",
        "label": "SINGULARITY STUDIOS LLC",
        "x": 30,
        "y": 2930,
        "w": 340,
        "h": 240,
        "cols": 1,
        "ids": [
            "singularity"
        ]
    },
    {
        "id": "rose-company",
        "label": "ROSE ENTERPRISES",
        "x": 400,
        "y": 2930,
        "w": 340,
        "h": 240,
        "cols": 1,
        "ids": [
            "rose"
        ]
    }
];
        const nodeById = Object.fromEntries(NODES.map(n => [n.id, n]));
        SECTIONS.forEach(section => {
            const rows = Math.ceil(section.ids.length / section.cols);
            section.ids.forEach((id, index) => {
                const n = nodeById[id];
                if (!n) throw new Error('Unknown ecosystem project: ' + id);
                n.section = section.id;
                n.x = section.x + section.w / section.cols * (index % section.cols + .5);
                n.y = section.y + 72 + (section.h - 100) / rows * (Math.floor(index / section.cols) + .5);
            });
        });
        NODES.filter(n => !n.section).forEach(n => { n.y -= 100; });
        // The parent company and its projects share a boundary. Product use is a
        // separate relationship, represented by an edge across those boundaries.
        nodeById.fervent.sub = [];
        nodeById.fervent.stage = 'ENTITY';
        nodeById.fervent.concept = false;
        nodeById.fervent.blurb = 'Independent creative and advocacy business behind Tryzantha, Myravelle and its campaign projects.';
        svg.setAttribute('viewBox', '0 0 1500 3210');

        const FAN = ['manifest', 'talos', 'eros', 'psyche', 'perpetuity', 'unravel', 'slipstream',
                     'plexus', 'positronic', 'apollo', 'lore', 'webservices',
                     'meld', 'holo', 'relay'];

        // A conceptual pairing, kept separate from the technical dependency edges.
        const CORE_PAIRS = [{ ids: ['eros', 'psyche'], label: 'COMPLEMENTARY CORES' }];

        const NEXUS_DEMOS = new Set(NODES.filter(n => n.nexusDemo).map(n => n.id));
        const FAN_ROUTES = {
            unravel: { ctrl: { x: 575, y: 625 }, ctrl2: { x: 550, y: 650 } },
            slipstream: { ctrl: { x: 725, y: 375 }, ctrl2: { x: 825, y: 275 } },
            holo: { ctrl: { x: 725, y: 600 }, ctrl2: { x: 725, y: 625 } }
        };

        // bend > 0 bows the edge to the right of travel direction; < 0 to the left.
        // ctrl pins a quadratic handle; ctrl2 adds a cubic handle for routes around multiple boxes.
        const EDGES = [
            ...FAN.map(t => ({ from: NEXUS_DEMOS.has(t) ? 'nexus' : 'parallax', to: t, fan: !NEXUS_DEMOS.has(t), dashed: NEXUS_DEMOS.has(t), nexus: NEXUS_DEMOS.has(t), ...FAN_ROUTES[t] })),
            { from: 'plexus', to: 'thetalabs', bend: -45 },
            { from: 'slipstream', to: 'thetalabs', dashed: true },
            { from: 'wphyperdrive', to: 'thetalabs' },
            { from: 'apollo', to: 'wphyperdrive', dashed: true, bend: 50 },
            { from: 'plexus', to: 'acquire', dashed: true, bend: -45 },
            { from: 'positronic', to: 'acquire', dashed: true },
            { from: 'positronic', to: 'eyecommerce', dashed: true, ctrl: { x: 1050, y: 475 }, ctrl2: { x: 1200, y: 400 } },
            { from: 'acquire', to: 'unblurred', dashed: true },
            { from: 'eyecommerce', to: 'unblurred' },
            { from: 'acquire', to: 'hearthglow', dashed: true },
            { from: 'acquire', to: 'prepped', dashed: true },
            { from: 'acquire', to: 'atlas', dashed: true, bend: -120 },

            { from: 'prepped', to: 'savorsocial' },
            { from: 'prepped', to: 'indulge' },
            { from: 'prepped', to: 'hearthglow' },
            { from: 'prepped', to: 'tarragon' },
            { from: 'varta', to: 'atlas', both: true },

            { from: 'atlas', to: 'fervent' },
            { from: 'petrichor', to: 'thetalabs', dashed: true },
            { from: 'positronic', to: 'meld', ctrl: { x: 850, y: 500 }, ctrl2: { x: 700, y: 850 } },
            { from: 'meld', to: 'levelup', dashed: true, ctrl: { x: 575, y: 100 } },
            { from: 'positronic', to: 'holo', dashed: true, bend: 100 },
            { from: 'plexus', to: 'holo', dashed: true, bend: 110 },
            { from: 'holo', to: 'relay', dashed: true },
            { from: 'relay', to: 'atlas', dashed: true },
            { from: 'plexuscore', to: 'savorsocial', dashed: true, social: true, ctrl: { x: 1400, y: 330 } },
            { from: 'plexuscore', to: 'psyche', dashed: true, social: true, ctrl: { x: 490, y: 135 }, ctrl2: { x: 470, y: 580 } },
            { from: 'psyche', to: 'onlyfriends', dashed: true, social: true },
            { from: 'psyche', to: 'gofundyourself', dashed: true, social: true },
            { from: 'plexuscore', to: 'partition', dashed: true, social: true, ctrl: { x: 775, y: 500 }, ctrl2: { x: 1000, y: 825 } }
        ];

        EDGES.push(...[{"from": "gofundyourself", "to": "nexus", "dashed": true}, {"from": "gofundyourself", "to": "acquire", "dashed": true}, {"from": "gofundyourself", "to": "eros", "dashed": true}, {"from": "gofundyourself", "to": "tarragon", "dashed": true}, {"from": "gofundyourself", "to": "holo", "dashed": true}, {"from": "gofundyourself", "to": "pulsar", "dashed": true}, {"from": "gofundyourself", "to": "familycookbook", "dashed": true}, {"from": "gofundyourself", "to": "tck", "dashed": true}, {"from": "gofundyourself", "to": "frenetic", "dashed": true}, {"from": "parallax", "to": "nexus", "dashed": true}, {"from": "parallaxcli", "to": "slipstream", "dashed": true}, {"from": "freighter", "to": "archer", "dashed": false}, {"from": "archer", "to": "slipstreamzh", "dashed": true}, {"from": "freighter", "to": "wphyperdrive", "dashed": true}, {"from": "freighter", "to": "lambogento", "dashed": true}, {"from": "freighter", "to": "standalone", "dashed": true}, {"from": "freighter", "to": "gofundyourself", "dashed": true}, {"from": "helm", "to": "slipstream", "dashed": true}, {"from": "helm", "to": "positronic", "dashed": true}, {"from": "zh", "to": "zhql", "dashed": false}, {"from": "zh", "to": "zzh", "dashed": true}, {"from": "zefram", "to": "archer", "dashed": true}, {"from": "shchytok", "to": "station", "dashed": true}, {"from": "nucleus", "to": "positronic", "dashed": false}, {"from": "nebula", "to": "nucleus", "dashed": true}, {"from": "plexuscore", "to": "plexus", "dashed": false}, {"from": "continuum", "to": "slipstream", "dashed": true}, {"from": "continuum", "to": "quark", "dashed": true}, {"from": "pulsar", "to": "relay", "dashed": true}, {"from": "pulsar", "to": "kinetic", "dashed": true}, {"from": "holo", "to": "tarragon", "dashed": true}, {"from": "tarragon", "to": "tarragonextract", "dashed": true}, {"from": "tarragonextract", "to": "provenance", "dashed": true}, {"from": "tarragon", "to": "livingcookbook", "dashed": true}, {"from": "tarragon", "to": "familycookbook", "dashed": true}, {"from": "holo", "to": "familycookbook", "dashed": true}, {"from": "prepped", "to": "tck", "dashed": false}, {"from": "wphyperdrive", "to": "petrichor", "dashed": true}, {"from": "acquire", "to": "eyecommerce", "dashed": true}, {"from": "acquire", "to": "indulge", "dashed": true}, {"from": "psyche", "to": "partition", "dashed": true}, {"from": "fervent", "to": "tryzantha", "dashed": false}, {"from": "fervent", "to": "myravelle", "dashed": false}, {"from": "fervent", "to": "zapovit", "dashed": false}, {"from": "tryzantha", "to": "nemtsov", "dashed": false}, {"from": "talbottech", "to": "frenetic", "dashed": true}, {"from": "thetalabs", "to": "epsilon", "dashed": true}, {"from": "parallax", "to": "gofundyourself", "dashed": true}, {"from": "parallax", "to": "acquire", "dashed": true}, {"from": "parallax", "to": "nexus", "dashed": true}, {"from": "parallax", "to": "tarragon", "dashed": true}, {"from": "parallax", "to": "holo", "dashed": true}]);
        EDGES.forEach(edge => {
            const a = nodeById[edge.from], b = nodeById[edge.to];
            if (a?.section || b?.section) {
                delete edge.ctrl; delete edge.ctrl2; delete edge.bend;
                edge.crossSection = a?.section !== b?.section;
            }
        });

        const ZONES = [


            { label: 'INTERNAL PIPELINE', x: 310, y: 145 },
            { label: 'CORE SUITE', x: 975, y: 92 },



            { label: 'STANDALONE PRODUCTS', x: 640, y: 718 }
        ];

        const el = (tag, attrs, parent) => {
            const e = document.createElementNS(NS, tag);
            for (const k in attrs) e.setAttribute(k, attrs[k]);
            if (parent) parent.appendChild(e);
            return e;
        };

        // Keep lines and arrowheads clear of labels even on highlighted paths.
        const defs = el('defs', {}, svg);
        const headingMask = el('mask', {
            id: 'eco-label-clearance', maskUnits: 'userSpaceOnUse',
            x: 0, y: 0, width: 1500, height: svg.viewBox.baseVal.height
        }, defs);
        el('rect', { x: 0, y: 0, width: 1500, height: svg.viewBox.baseVal.height, fill: 'white' }, headingMask);
        SECTIONS.forEach(section => {
            el('rect', { x: section.x + 1, y: section.y + 1, width: section.w - 2, height: 48, fill: 'black' }, headingMask);
        });
        const sectionLayer = el('g', { 'aria-hidden': 'true' }, svg);
        SECTIONS.forEach(section => {
            const group = el('g', { 'data-company-section': section.id }, sectionLayer);
            el('rect', { x: section.x, y: section.y, width: section.w, height: section.h, rx: 14,
                fill: 'rgba(255,255,255,.025)', stroke: 'rgba(196,181,253,.22)' }, group);
            const title = el('text', { x: section.x + 22, y: section.y + 32, fill: '#c4b5fd',
                'font-size': 12, 'font-weight': 800, 'letter-spacing': '1.3px' }, group);
            title.textContent = section.label;
        });
        const pairLayer = el('g', { 'aria-hidden': 'true', 'pointer-events': 'none' }, svg);
        const edgeLayer = el('g', { mask: 'url(#eco-label-clearance)' }, svg);
        const chevLayer = el('g', { mask: 'url(#eco-label-clearance)' }, svg);
        const nodeLayer = el('g', {}, svg);

        ZONES.forEach(z => {
            const t = el('text', { x: z.x, y: z.y, 'text-anchor': 'middle', class: 'zone' }, svg);
            t.textContent = z.label;
            const bounds = t.getBBox();
            el('rect', { x: bounds.x - 6, y: bounds.y - 5, width: bounds.width + 12, height: bounds.height + 10, fill: 'black' }, headingMask);
        });

        // --- Nodes ---
        // Use existing brand marks without adding another project node.
        const LOGOS = {
            slipstream: '/assets/logos/slipstream-mark.svg',
            plexus: '/assets/logos/plexus-mark.svg',
            positronic: '/assets/logos/positronic-mark.svg',
            thetalabs: '/assets/Theta Symbol.svg',
            tarragon: '/assets/logos/tarragon-mark.svg',
            eyecommerce: '/assets/logos/eyecommerce-mark.svg',
            atlas: '/assets/logos/atlas-mark.svg',
            nucleus: '/assets/nucleus/nucleus-logo.svg',
            continuum: '/assets/council/assets/continuum.svg',
            quark: '/assets/council/assets/quark.svg',
            pulsar: '/assets/council/assets/pulsar.svg'
        };
        const byId = {};
        NODES.forEach(n => {
            byId[n.id] = n;
            const accent = n.color || GROUPS[n.group].color;
            const g = el('g', { class: 'node', 'data-id': n.id, tabindex: '0', role: n.core ? 'group' : 'img' }, nodeLayer);
            g.setAttribute('aria-label', n.name + (n.stage ? ' — ' + n.stage : '') + '. ' + n.blurb.replace(/&rsquo;/g, '’')
                + (n.source ? ' Source: ' + SOURCE[n.source].chip.toLowerCase() + '.' : ''));

            const lines = [];
            let coreElements = [], coreTop;
            const nameSize = n.big ? 19 : 13;
            let yCursor = 0;

            if (n.mark) {
                n.markImg = el('image', { href: n.mark, width: 46, height: 46, x: -23, y: yCursor }, g);
                yCursor += 54;
            }
            const nameText = el('text', {
                x: 0, y: yCursor + nameSize, 'text-anchor': 'middle',
                'font-size': nameSize, 'font-weight': 800, 'letter-spacing': '.08em',
                fill: (n.group === 'pipeline' || n.group === 'bets') ? 'rgba(255,255,255,.65)'
                    : (n.color || (n.group === 'commerce' ? C.commerce : (n.group === 'labs' ? C.labs : (n.group === 'products' ? C.products : '#ffffff'))))
            }, g);
            nameText.textContent = n.name;
            lines.push(nameText);
            yCursor += nameSize + 4;

            if (n.stage) {
                const st = el('text', {
                    x: 0, y: yCursor + 9, 'text-anchor': 'middle',
                    'font-size': 8.5, 'font-weight': 700, 'letter-spacing': '.18em',
                    fill: STAGE_COLORS[n.stage] || 'rgba(255,255,255,.5)'
                }, g);
                st.textContent = n.stage;
                lines.push(st);
                yCursor += 15;
            }
            (n.sub || []).forEach(s => {
                const t = el('text', {
                    x: 0, y: yCursor + 11, 'text-anchor': 'middle',
                    'font-size': 10.5, 'font-weight': 600, fill: 'rgba(255,255,255,.6)'
                }, g);
                t.textContent = s;
                lines.push(t);
                yCursor += 15;
            });
            if (n.core) {
                coreTop = yCursor + 9;
                const coreBox = el('rect', {
                    x: -91, y: coreTop, width: 182, height: 80, rx: 8,
                    fill: 'rgba(110,231,183,.09)', stroke: C.oss, 'stroke-opacity': .65,
                    'data-core': n.core.id
                }, g);
                const coreTitle = el('text', {
                    x: 0, y: coreTop + 23, 'text-anchor': 'middle',
                    'font-size': 14, 'font-weight': 800, 'letter-spacing': '.16em', fill: C.oss
                }, g);
                coreTitle.textContent = n.core.name;
                coreTitle.setAttribute('fill', n.core.color || C.oss);
                const coreDescription = el('text', {
                    x: 0, y: coreTop + 42, 'text-anchor': 'middle',
                    'font-size': 8, 'font-weight': 700, 'letter-spacing': '.07em', fill: 'rgba(255,255,255,.75)'
                }, g);
                coreDescription.textContent = n.core.description;
                const coreCaption = el('text', {
                    x: 0, y: coreTop + 62, 'text-anchor': 'middle',
                    'font-size': 7.5, 'font-weight': 800, 'letter-spacing': '.16em', fill: C.oss
                }, g);
                coreCaption.textContent = SOURCE.open.badge;
                const coreBadgeBounds = coreCaption.getBBox();
                const coreBadge = el('rect', {
                    x: coreBadgeBounds.x - 6, y: coreBadgeBounds.y - 2.5,
                    width: coreBadgeBounds.width + 12, height: coreBadgeBounds.height + 5, rx: 4,
                    fill: 'rgba(110,231,183,.08)', stroke: C.oss, 'stroke-opacity': .6, 'stroke-width': 1
                });
                g.insertBefore(coreBadge, coreCaption);
                coreElements = [coreBox, coreTitle, coreDescription, coreBadge, coreCaption];
                lines.push(...coreElements);
                yCursor = coreTop + 84;
            }
            const src = n.source ? SOURCE[n.source] : null;
            if (src && src.badge && !n.core) {
                const t = el('text', {
                    x: 0, y: yCursor + 13, 'text-anchor': 'middle',
                    'font-size': 7.5, 'font-weight': 800, 'letter-spacing': '.16em', fill: C.oss
                }, g);
                t.textContent = src.badge;
                const tb = t.getBBox();
                const pill = el('rect', {
                    x: tb.x - 6, y: tb.y - 2.5, width: tb.width + 12, height: tb.height + 5, rx: 4,
                    fill: 'rgba(110,231,183,.08)', stroke: C.oss, 'stroke-opacity': .6, 'stroke-width': 1
                });
                g.insertBefore(pill, t);
                lines.push(pill, t);
                yCursor += 20;
            }

            if (n.nexusDemo) {
                const demo = el('text', {
                    x: 0, y: yCursor + 10, 'text-anchor': 'middle',
                    'font-size': 7.5, 'font-weight': 800, 'letter-spacing': '.14em', fill: C.oss
                }, g);
                demo.textContent = 'NEXUS DEMO';
                lines.push(demo);
                yCursor += 15;
            }

            g.setAttribute('transform', 'translate(' + n.x + ',' + n.y + ')');
            const bb = g.getBBox();
            const padX = 13, padY = 9;
            n.w = bb.width + padX * 2 + (LOGOS[n.id] ? 56 : 0);
            n.h = bb.height + padY * 2;
            // Recenter content vertically around (0,0)
            const shift = -(bb.y + bb.height / 2);
            [...(n.markImg ? [n.markImg] : []), ...lines].forEach(t => {
                t.setAttribute('y', parseFloat(t.getAttribute('y')) + shift);
            });
            const rect = el('rect', {
                x: -n.w / 2, y: -n.h / 2, width: n.w, height: n.h, rx: 10,
                fill: 'rgba(255,255,255,.05)',
                stroke: accent, 'stroke-opacity': (n.group === 'pipeline' || n.group === 'bets') ? .45 : (n.color || n.group === 'commerce' || n.group === 'labs' || n.group === 'products' ? .55 : .3),
                'stroke-dasharray': n.concept ? '4 4' : 'none'
            });
            g.insertBefore(rect, g.firstChild);
            if (LOGOS[n.id]) {
                el('image', { href: LOGOS[n.id], x: -n.w / 2 + 7, y: -10, width: 20, height: 20, 'aria-hidden': 'true' }, g);
            }
            if (n.core) {
                const core = {
                    ...n.core, parent: n.id, group: 'foundation', stage: '', source: 'open',
                    x: n.x, y: n.y + coreTop + shift + 40, w: 182, h: 80
                };
                byId[core.id] = core;
                const coreGroup = el('g', {
                    class: 'node', 'data-id': core.id, 'data-parent': n.id, tabindex: '0', role: 'img',
                    'aria-label': core.name + '. ' + core.blurb + ' Source: open source.'
                }, g);
                coreElements.forEach(element => coreGroup.appendChild(element));
            }
        });

        // Mask every card, not just the connection's endpoints: translucent
        // node fills otherwise let unrelated edges run through names and logos.
        NODES.forEach(n => {
            el('rect', { x: n.x - n.w / 2 - 4, y: n.y - n.h / 2 - 4,
                width: n.w + 8, height: n.h + 8, rx: 12, fill: 'black',
                'data-clearance-for': n.id }, headingMask);
        });

        // Draw a quiet shared enclosure without adding a dependency arrow.
        CORE_PAIRS.forEach(pair => {
            const members = pair.ids.map(id => byId[id]);
            const left = Math.min(...members.map(n => n.x - n.w / 2)) - 12;
            const right = Math.max(...members.map(n => n.x + n.w / 2)) + 12;
            const top = Math.min(...members.map(n => n.y - n.h / 2)) - 23;
            const bottom = Math.max(...members.map(n => n.y + n.h / 2)) + 10;
            const group = el('g', { class: 'core-pair', 'data-members': pair.ids.join(' ') }, pairLayer);
            el('rect', {
                x: left, y: top, width: right - left, height: bottom - top, rx: 16,
                fill: 'rgba(196,181,253,.025)', stroke: C.labs, 'stroke-opacity': .3
            }, group);
            const label = el('text', {
                x: (left + right) / 2, y: top + 14, 'text-anchor': 'middle',
                'font-size': 8, 'font-weight': 800, 'letter-spacing': '.12em', fill: C.labs
            }, group);
            label.textContent = pair.label;
            const bounds = label.getBBox();
            el('rect', { x: bounds.x - 6, y: bounds.y - 4, width: bounds.width + 12, height: bounds.height + 8, fill: 'black' }, headingMask);
        });

        // --- Edges (sampled, trimmed to node borders) ---
        const inRect = (p, n, m) => Math.abs(p.x - n.x) <= n.w / 2 + m && Math.abs(p.y - n.y) <= n.h / 2 + m;

        const sample = (a, b, bend, ctrl, ctrl2) => {
            const pts = [];
            const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
            const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy) || 1;
            const cx = ctrl ? ctrl.x : mx + (bend || 0) * (-dy / len);
            const cy = ctrl ? ctrl.y : my + (bend || 0) * (dx / len);
            for (let i = 0; i <= 64; i++) {
                const t = i / 64, u = 1 - t;
                pts.push(ctrl2
                    ? { x: u ** 3 * a.x + 3 * u * u * t * cx + 3 * u * t * t * ctrl2.x + t ** 3 * b.x,
                        y: u ** 3 * a.y + 3 * u * u * t * cy + 3 * u * t * t * ctrl2.y + t ** 3 * b.y }
                    : { x: u * u * a.x + 2 * u * t * cx + t * t * b.x,
                        y: u * u * a.y + 2 * u * t * cy + t * t * b.y });
            }
            return pts;
        };

        const adjacency = {};
        EDGES.forEach(e => {
            const a = byId[e.from], b = byId[e.to];
            let pts = sample(a, b, e.bend, e.ctrl, e.ctrl2);
            const margin = 5;
            let i0 = 0; while (i0 < pts.length && inRect(pts[i0], a, margin)) i0++;
            let i1 = pts.length - 1; while (i1 >= 0 && inRect(pts[i1], b, margin)) i1--;
            if (i0 >= i1) return;
            pts = pts.slice(Math.max(0, i0 - 1), i1 + 2);

            const d = 'M' + pts.map(p => p.x.toFixed(1) + ' ' + p.y.toFixed(1)).join('L');
            const color = e.social ? C.plexus : (e.nexus ? C.oss : (e.fan ? null : (byId[e.to].color || GROUPS[byId[e.to].group].color)));
            const path = el('path', {
                d, class: 'edge' + (e.fan ? ' fan' : '') + (e.dashed ? ' dashed' : '') + (e.crossSection ? ' cross-section' : ''),
                'data-from': e.from, 'data-to': e.to
            }, edgeLayer);
            if (!e.fan) { path.setAttribute('stroke', color); path.setAttribute('stroke-opacity', e.nexus || e.social ? '.65' : '.4'); }

            // Direction chevrons (skip the hub fan — direction there is just "radiates out")
            if (!e.fan) {
                const lens = [0]; let L = 0;
                for (let i = 1; i < pts.length; i++) { L += Math.hypot(pts[i].x - pts[i-1].x, pts[i].y - pts[i-1].y); lens.push(L); }
                const at = f => {
                    const target = L * f; let i = lens.findIndex(v => v >= target);
                    if (i < 1) i = 1;
                    const p = pts[i], q = pts[i - 1];
                    return { x: p.x, y: p.y, ang: Math.atan2(p.y - q.y, p.x - q.x) * 180 / Math.PI };
                };
                const chev = (f, flip) => {
                    const p = at(f);
                    el('path', {
                        d: 'M-4 -3.5 L2.5 0 L-4 3.5', fill: 'none', 'stroke-width': 1.5,
                        stroke: color, 'stroke-opacity': '.8', class: 'chev' + (e.crossSection ? ' cross-section' : ''),
                        'data-from': e.from, 'data-to': e.to,
                        transform: 'translate(' + p.x.toFixed(1) + ',' + p.y.toFixed(1) + ') rotate(' + (p.ang + (flip ? 180 : 0)).toFixed(1) + ')'
                    }, chevLayer);
                };
                chev(e.both ? 0.62 : 0.55, false);
                if (e.both) chev(0.4, true);
            }

            (adjacency[e.from] = adjacency[e.from] || new Set()).add(e.to);
            (adjacency[e.to] = adjacency[e.to] || new Set()).add(e.from);
        });

        // --- Hover / focus / tap interactions ---
        const stageChip = s => s ? '<span style="font-size:10px;font-weight:800;letter-spacing:.15em;color:' + (STAGE_COLORS[s] || '#fff') + '">' + s + '</span>' : '';
        const sourceChip = s => s ? '<div style="margin-top:6px;font-size:10px;font-weight:800;letter-spacing:.15em;color:' + (s === 'closed' ? 'rgba(255,255,255,.5)' : C.oss) + '">' + SOURCE[s].chip + '</div>' : '';

        function focus(n) {
            svg.classList.add('focused');
            const selected = new Set([n.id, ...(n.core ? [n.core.id] : [])]);
            const near = new Set(selected);
            CORE_PAIRS.filter(pair => pair.ids.includes(n.id)).forEach(pair => pair.ids.forEach(id => near.add(id)));
            selected.forEach(id => (adjacency[id] || []).forEach(neighbor => near.add(neighbor)));
            // Keep the containing product visible when its nested core is highlighted.
            [...near].forEach(id => { if (byId[id].parent) near.add(byId[id].parent); });
            nodeLayer.querySelectorAll('.node').forEach(g => {
                const id = g.getAttribute('data-id');
                g.classList.toggle('hl', id === n.id || near.has(id));
            });
            svg.querySelectorAll('.edge, .chev').forEach(p => {
                p.classList.toggle('hl', selected.has(p.getAttribute('data-from')) || selected.has(p.getAttribute('data-to')));
            });
            tip.innerHTML =
                '<div style="display:flex;align-items:baseline;gap:10px;justify-content:space-between">' +
                '<span style="font-weight:800;letter-spacing:.06em;color:' + (n.color || GROUPS[n.group].color) + '">' + n.name + '</span>' +
                stageChip(n.stage) + '</div>' +
                sourceChip(n.source) +
                (n.nexusDemo ? '<div style="margin-top:6px;font-size:10px;font-weight:800;color:' + C.oss + '">FIRST NEXUS DEMONSTRATION · PLANNED</div>' : '') +
                '<div style="margin-top:6px;line-height:1.5;color:rgba(255,255,255,.75)">' + n.blurb + '</div>' +
                '<div style="margin-top:6px;font-size:10px;letter-spacing:.15em;text-transform:uppercase;color:rgba(255,255,255,.4)">' + (SECTIONS.find(section => section.id === n.section)?.label || GROUPS[n.group].label) + '</div>';
            tip.classList.remove('hidden');
            const scale = wrap.clientWidth / svg.viewBox.baseVal.width;
            const left = Math.min(Math.max(8, n.x * scale - 145), wrap.clientWidth - 298);
            let top = (n.y + n.h / 2) * scale + 10;
            if (top + 150 > wrap.clientHeight) top = (n.y - n.h / 2) * scale - tipHeightGuess(n) - 10;
            tip.style.left = left + 'px';
            tip.style.top = top + 'px';
        }
        const tipHeightGuess = n => 92 + (n.source ? 16 : 0) + Math.ceil(n.blurb.length / 38) * 8;

        function blur() {
            svg.classList.remove('focused');
            svg.querySelectorAll('.hl').forEach(x => x.classList.remove('hl'));
            tip.classList.add('hidden');
        }

        nodeLayer.querySelectorAll('.node').forEach(g => {
            const n = byId[g.getAttribute('data-id')];
            g.addEventListener('mouseenter', () => focus(n));
            g.addEventListener('mouseleave', ev => {
                const next = ev.relatedTarget?.closest?.('.node');
                if (n.parent && next?.getAttribute('data-id') === n.parent) focus(byId[n.parent]);
                else blur();
            });
            g.addEventListener('focus', () => focus(n));
            g.addEventListener('blur', blur);
            g.addEventListener('click', ev => { ev.stopPropagation(); focus(n); });
        });
        svg.addEventListener('click', blur);
        const index = document.getElementById('eco-index');
        if (index) {
            const groups = [{ label: 'PARALLAX · SHARED PRODUCTS', ids: NODES.filter(n => !n.section).map(n => n.id) }, ...SECTIONS];
            groups.forEach(section => {
                const block = document.createElement('section');
                const heading = document.createElement('h3');
                heading.textContent = section.label;
                block.appendChild(heading);
                section.ids.forEach(id => {
                    const n = nodeById[id];
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.textContent = n.name + (n.stage ? ' · ' + n.stage : '');
                    button.addEventListener('click', () => {
                        nodeLayer.querySelector('[data-id="' + id + '"]').scrollIntoView({ block: 'center', inline: 'center' });
                        focus(n);
                    });
                    block.appendChild(button);
                });
                index.appendChild(block);
            });
        }
    })();
// Open the full map for links to its content.
function revealEcosystemAnchor(){
 const target=document.getElementById(location.hash.slice(1));
 if(!target)return;
 let parent=target;
 while(parent){if(parent.tagName==='DETAILS')parent.open=true;parent=parent.parentElement;}
}
revealEcosystemAnchor();
window.addEventListener('hashchange',revealEcosystemAnchor);
