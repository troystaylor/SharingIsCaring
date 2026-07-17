FROM python:3.12-slim

# System dependencies for compiled Python packages
RUN apt-get update && apt-get install -y --no-install-recommends \
    gcc g++ libffi-dev libxml2-dev libxslt1-dev \
    r-base r-base-dev \
    && rm -rf /var/lib/apt/lists/*

# Python libraries — Microsoft/Office formats
RUN pip install --no-cache-dir \
    python-docx openpyxl xlsxwriter xlrd python-pptx \
    pypdf reportlab extract-msg olefile mammoth \
    icalendar vobject

# Python libraries — Data & analysis
RUN pip install --no-cache-dir \
    pandas numpy matplotlib pillow pydantic

# Python libraries — Productivity
RUN pip install --no-cache-dir \
    jinja2 python-dateutil qrcode humanize tabulate cryptography

# Python libraries — Web & parsing
RUN pip install --no-cache-dir \
    requests beautifulsoup4 lxml feedparser pyyaml

# Python libraries — MCP & developer tools
RUN pip install --no-cache-dir \
    mcp httpx fastapi uvicorn jsonschema

# R packages (tidyverse, jsonlite, openxlsx)
RUN Rscript -e "install.packages(c('tidyverse','jsonlite','openxlsx'), repos='https://cloud.r-project.org', quiet=TRUE)"

# Workspace directory
RUN mkdir -p /workspace
WORKDIR /workspace

# Keep container running (sandbox lifecycle managed by ACA)
CMD ["sleep", "infinity"]
