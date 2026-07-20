FROM python:3.12-slim

LABEL maintainer="Troy Taylor <troy@troystaylor.com>"
LABEL description="Azure AI Image Search — FastMCP server for multimodal image search"

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY server.py .
COPY viewer.html .

EXPOSE 8000

CMD ["python", "server.py"]
