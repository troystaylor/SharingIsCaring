const GITHUB_API = "https://api.github.com";
const GITHUB_TOKEN = process.env.GITHUB_TOKEN; // Server-side token for code search (requires auth)
const HEADERS = {
    "User-Agent": "AgentSkills-MCP/1.0",
    Accept: "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
};

function getHeaders(token) {
    const h = { ...HEADERS };
    const t = token || GITHUB_TOKEN;
    if (t) h.Authorization = `token ${t}`;
    return h;
}

async function githubFetch(url, token) {
    const res = await fetch(url, { headers: getHeaders(token) });
    if (!res.ok) throw new Error(`GitHub ${res.status}: ${await res.text()}`);
    return res.json();
}

// --- Tool implementations ---

async function getCuratedRegistry() {
    return {
        standard: "agentskills.io",
        focus: "Skills useful for M365 Copilot Cowork — guidance, workflows, and executable procedures.",
        repositories: [
            {
                repository: "dotnet/skills",
                description: "Microsoft .NET AI/ML technology selection, architecture patterns, ASP.NET Core, and data access.",
                skills_paths: ["plugins/dotnet-ai/skills", "plugins/dotnet-aspnetcore/skills", "plugins/dotnet-data/skills"],
                category: "Architecture & AI Patterns",
            },
            {
                repository: "softaworks/agent-toolkit",
                description: "Professional communication, requirements clarity, database schema design, and documentation skills.",
                skills_paths: ["skills"],
                category: "Business & Communication",
                recommended_skills: ["professional-communication", "requirements-clarity", "database-schema-designer", "humanizer"],
            },
            {
                repository: "mcp-use/mcp-use",
                description: "OpenAPI-to-MCP conversion — turning REST API specs into MCP tool definitions.",
                skills_paths: ["skills"],
                category: "Connector & API Design",
            },
            {
                repository: "Forward-Future/loopy",
                description: "Practical AI agent loop patterns for multi-step reasoning and orchestration.",
                skills_paths: ["skills"],
                category: "Workflow Patterns",
            },
            {
                repository: "anthropics/skills",
                description: "Anthropic's reference skills — templates and examples for the agentskills.io standard.",
                skills_paths: ["skills"],
                category: "Reference & Templates",
            },
        ],
    };
}

async function searchSkills({ query, github_token }) {
    const q = encodeURIComponent(`${query} filename:SKILL.md`);
    const url = `${GITHUB_API}/search/code?q=${q}&per_page=10`;
    const headers = getHeaders(github_token);
    headers.Accept = "application/vnd.github.text-match+json";
    const res = await fetch(url, { headers });
    if (!res.ok) throw new Error(`GitHub ${res.status}: ${await res.text()}`);
    const data = await res.json();

    return {
        total_count: data.total_count,
        results: (data.items || []).map((i) => ({
            repository: i.repository?.full_name,
            path: i.path,
            html_url: i.html_url,
            fragments: (i.text_matches || []).map((tm) => tm.fragment).slice(0, 2),
        })),
    };
}

async function listSkills({ owner, repo, skills_path, github_token }) {
    const url = `${GITHUB_API}/repos/${owner}/${repo}/contents/${skills_path}?ref=main`;
    const data = await githubFetch(url, github_token);
    const dirs = (Array.isArray(data) ? data : []).filter((i) => i.type === "dir");
    return {
        repository: `${owner}/${repo}`,
        skills_path,
        skills: dirs.map((d) => ({ name: d.name, path: d.path })),
        count: dirs.length,
    };
}

async function getSkill({ owner, repo, skill_path, github_token }) {
    const url = `${GITHUB_API}/repos/${owner}/${repo}/contents/${skill_path}/SKILL.md?ref=main`;
    const data = await githubFetch(url, github_token);
    const content = Buffer.from(data.content, "base64").toString("utf-8");
    return {
        repository: `${owner}/${repo}`,
        skill_path,
        html_url: data.html_url,
        content,
    };
}

// --- Registration ---

export function registerGithubTools(server, z) {
    server.tool(
        "get_curated_registry",
        "Get the curated registry of known agentskills.io skill repositories. Use first to discover available skill sources.",
        {},
        async () => ({ content: [{ type: "text", text: JSON.stringify(await getCuratedRegistry()) }] })
    );

    server.tool(
        "search_skills",
        "Search across all of GitHub for Agent Skills (SKILL.md files) by keyword. Returns matching skill files with context fragments.",
        {
            query: z.string().describe("Search keywords (e.g., 'database migration', 'REST API design', 'testing strategy')"),
            github_token: z.string().optional().describe("Optional GitHub PAT for higher rate limits. Omit to use unauthenticated access (60 req/hr)."),
        },
        async ({ query, github_token }) => ({ content: [{ type: "text", text: JSON.stringify(await searchSkills({ query, github_token })) }] })
    );

    server.tool(
        "list_skills",
        "List all skills in a repository at a given path. Returns skill names for progressive discovery.",
        {
            owner: z.string().describe("Repository owner (e.g., dotnet, anthropics)"),
            repo: z.string().describe("Repository name (e.g., skills, boost)"),
            skills_path: z.string().describe("Path to skills directory (e.g., plugins/dotnet-ai/skills)"),
            github_token: z.string().optional().describe("Optional GitHub PAT for higher rate limits."),
        },
        async ({ owner, repo, skills_path, github_token }) => ({
            content: [{ type: "text", text: JSON.stringify(await listSkills({ owner, repo, skills_path, github_token })) }],
        })
    );

    server.tool(
        "get_skill",
        "Load the full SKILL.md content for a specific skill. Returns the complete instructions including workflows, code examples, and validation checklists.",
        {
            owner: z.string().describe("Repository owner"),
            repo: z.string().describe("Repository name"),
            skill_path: z.string().describe("Full path to skill directory (e.g., plugins/dotnet-ai/skills/technology-selection)"),
            github_token: z.string().optional().describe("Optional GitHub PAT for higher rate limits."),
        },
        async ({ owner, repo, skill_path, github_token }) => ({
            content: [{ type: "text", text: JSON.stringify(await getSkill({ owner, repo, skill_path, github_token })) }],
        })
    );
}
