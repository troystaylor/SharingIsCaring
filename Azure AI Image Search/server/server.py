"""
Azure AI Image Search — FastMCP server

Provides natural language image search over Azure AI Search multimodal indexes
with Azure Blob Storage image retrieval. Returns ImageContent for model inspection
and structured metadata for agent reasoning.

Inspired by: https://techcommunity.microsoft.com/blog/azuredevcommunityblog/beyond-text-returning-images-and-interactive-apps-from-mcp-servers/4535865
Sample: https://github.com/Azure-Samples/image-search-aisearch
"""

import base64
import io
import json
import logging
import os
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Annotated

import httpx
from azure.identity import DefaultAzureCredential
from azure.search.documents import SearchClient
from azure.search.documents.models import VectorizableImageUrlQuery, VectorizableTextQuery
from azure.storage.blob import generate_blob_sas, BlobSasPermissions
from azure.storage.blob.aio import BlobServiceClient
from fastapi import Depends, FastAPI, Header, HTTPException, UploadFile, File as FastAPIFile
from fastapi.responses import JSONResponse
from fastmcp import FastMCP
from mcp import types
from PIL import Image
from pydantic import BaseModel

# ─── Configuration ─────────────────────────────────────────────────────────────

SEARCH_ENDPOINT = os.environ.get("AZURE_SEARCH_ENDPOINT", "")
SEARCH_INDEX = os.environ.get("AZURE_SEARCH_INDEX", "")
SEARCH_KEY = os.environ.get("AZURE_SEARCH_KEY", "")

BLOB_CONNECTION_STRING = os.environ.get("AZURE_BLOB_CONNECTION_STRING", "")
BLOB_CONTAINER = os.environ.get("AZURE_BLOB_CONTAINER", "images")

# Vision endpoint for image vectorization (reverse image search)
VISION_ENDPOINT = os.environ.get("AZURE_VISION_ENDPOINT", "")
VISION_KEY = os.environ.get("AZURE_VISION_KEY", "")

# Lightweight mode: return URLs + metadata only, skip Blob image fetching
LIGHTWEIGHT_MODE = os.environ.get("LIGHTWEIGHT_MODE", "false").lower() in ("true", "1", "yes")

# API key for authentication
API_KEY = os.environ.get("API_KEY", "")

THUMBNAIL_MAX_SIZE = (512, 512)
IMAGE_VIEW_URI = "ui://azure-ai-image-search/viewer.html"

logger = logging.getLogger("ai-image-search")


# ─── Startup Validation ────────────────────────────────────────────────────────

def validate_config():
    """Fail fast if required configuration is missing."""
    errors = []

    if not SEARCH_ENDPOINT:
        errors.append("AZURE_SEARCH_ENDPOINT is required")
    if not SEARCH_INDEX:
        errors.append("AZURE_SEARCH_INDEX is required")
    if not SEARCH_KEY:
        # Only warn — DefaultAzureCredential may work
        logger.warning("AZURE_SEARCH_KEY not set; falling back to DefaultAzureCredential")

    if not LIGHTWEIGHT_MODE and not BLOB_CONNECTION_STRING:
        errors.append(
            "AZURE_BLOB_CONNECTION_STRING is required in full mode. "
            "Set LIGHTWEIGHT_MODE=true to skip Blob Storage."
        )

    if errors:
        for e in errors:
            logger.error(f"Configuration error: {e}")
        sys.exit(1)


# ─── Server ────────────────────────────────────────────────────────────────────

mcp = FastMCP(
    "Azure AI Image Search",
    version="1.0.0",
)


# ─── Helpers ───────────────────────────────────────────────────────────────────


def get_search_client() -> SearchClient:
    """Create Azure AI Search client."""
    if SEARCH_KEY:
        from azure.core.credentials import AzureKeyCredential
        credential = AzureKeyCredential(SEARCH_KEY)
    else:
        credential = DefaultAzureCredential()
    return SearchClient(
        endpoint=SEARCH_ENDPOINT,
        index_name=SEARCH_INDEX,
        credential=credential,
    )


def get_blob_service_client() -> BlobServiceClient:
    """Create async Blob Storage client."""
    return BlobServiceClient.from_connection_string(BLOB_CONNECTION_STRING)


def resize_image_bytes(image_bytes: bytes, max_size: tuple = THUMBNAIL_MAX_SIZE) -> bytes:
    """Resize image to thumbnail for token-efficient model inspection."""
    with Image.open(io.BytesIO(image_bytes)) as img:
        img.thumbnail(max_size)
        output = io.BytesIO()
        fmt = img.format or "JPEG"
        img.save(output, format=fmt)
        return output.getvalue()


def get_image_format(filename: str) -> str:
    """Get image format from filename extension."""
    ext = Path(filename).suffix.lower()
    return {
        ".jpg": "jpeg",
        ".jpeg": "jpeg",
        ".png": "png",
        ".gif": "gif",
        ".bmp": "bmp",
        ".webp": "webp",
    }.get(ext, "jpeg")


def get_mime_type(filename: str) -> str:
    """Get MIME type from filename."""
    fmt = get_image_format(filename)
    return f"image/{fmt}"


def run_search(search_client: SearchClient, query: str, max_results: int) -> list[dict]:
    """Run hybrid (text + vector) search, falling back to text-only if no embedding field."""
    select = ["metadata_storage_path", "metadata_storage_name", "verbalized_image"]

    try:
        results = search_client.search(
            search_text=query,
            top=max_results,
            vector_queries=[
                VectorizableTextQuery(
                    k_nearest_neighbors=max_results,
                    fields="embedding",
                    text=query,
                )
            ],
            select=select,
        )
        # Force evaluation to trigger any lazy errors
        return [r for r in results]
    except Exception as e:
        logger.warning(f"Vector search failed, falling back to text-only: {e}")
        results = search_client.search(
            search_text=query,
            top=max_results,
            select=select,
        )
        return [r for r in results]


# ─── Tools ─────────────────────────────────────────────────────────────────────


@mcp.tool(annotations={"readOnlyHint": True})
async def image_search(
    query: Annotated[str, "Natural language description of images to find (e.g., 'sunset over mountains')"],
    max_results: Annotated[int, "Maximum number of images to return (1-20)"] = 5,
) -> list:
    """
    Search for images matching a natural language query.
    Uses hybrid retrieval combining text and vector search over multimodal embeddings.
    Returns thumbnail images for model inspection and structured metadata.
    In lightweight mode, returns URLs and metadata only (no image bytes).
    """
    search_client = get_search_client()
    results = run_search(search_client, query, max_results)

    image_results: list[dict[str, str]] = []
    content_blocks: list = []

    if LIGHTWEIGHT_MODE:
        for result in results:
            image_results.append({
                "filename": result.get("metadata_storage_name", "unknown.jpg"),
                "display_name": result.get("metadata_storage_name", "unknown.jpg"),
                "description": result.get("verbalized_image", ""),
                "url": result.get("metadata_storage_path", ""),
            })
        content_blocks.append(types.TextContent(
            type="text",
            text=json.dumps({"query": query, "mode": "lightweight", "total_results": len(image_results), "results": image_results}),
        ))
        return content_blocks

    # Full mode: fetch thumbnails from Blob Storage
    blob_service_client = get_blob_service_client()

    async with blob_service_client:
        for result in results:
            filename = result.get("metadata_storage_name", "unknown.jpg")
            description = result.get("verbalized_image", "")
            storage_path = result.get("metadata_storage_path", "")

            try:
                blob_client = blob_service_client.get_blob_client(
                    container=BLOB_CONTAINER, blob=filename
                )
                stream = await blob_client.download_blob()
                image_bytes = await stream.readall()

                thumbnail_bytes = resize_image_bytes(image_bytes)
                mime_type = get_mime_type(filename)

                content_blocks.append(types.ImageContent(
                    type="image",
                    data=base64.b64encode(thumbnail_bytes).decode("utf-8"),
                    mimeType=mime_type,
                ))
                image_results.append({
                    "filename": filename,
                    "display_name": filename,
                    "description": description,
                    "storage_path": storage_path,
                })
            except Exception as e:
                image_results.append({
                    "filename": filename,
                    "display_name": filename,
                    "description": description,
                    "error": str(e),
                })

    # Append metadata as text after images
    content_blocks.append(types.TextContent(
        type="text",
        text=json.dumps({"query": query, "mode": "full", "total_results": len(image_results), "results": image_results}),
    ))
    return content_blocks


@mcp.tool(annotations={"readOnlyHint": True})
async def search_by_image(
    image_url: Annotated[str | None, "URL of an image to find similar images for"] = None,
    image_base64: Annotated[str | None, "Base64-encoded image data to find similar images for"] = None,
    max_results: Annotated[int, "Maximum number of similar images to return (1-20)"] = 5,
) -> list:
    """
    Find visually similar images by providing an image URL or base64 data.
    Uses Azure AI Vision multimodal embeddings to vectorize the input image,
    then searches the index for nearest neighbors. Provide either image_url or image_base64.
    """
    if not image_url and not image_base64:
        return [types.TextContent(type="text", text=json.dumps({"error": "Provide either image_url or image_base64"}))]

    if not image_url:
        return [types.TextContent(type="text", text=json.dumps({"error": "Reverse image search requires image_url. Base64 input not yet supported."}))]

    search_client = get_search_client()

    results = search_client.search(
        search_text="",
        top=max_results,
        vector_queries=[
            VectorizableImageUrlQuery(
                url=image_url,
                k_nearest_neighbors=max_results,
                fields="embedding",
            )
        ],
        select=["metadata_storage_path", "metadata_storage_name", "verbalized_image"],
    )

    image_results: list[dict[str, str]] = []
    content_blocks: list = []

    if LIGHTWEIGHT_MODE:
        for result in results:
            image_results.append({
                "filename": result.get("metadata_storage_name", "unknown.jpg"),
                "display_name": result.get("metadata_storage_name", "unknown.jpg"),
                "description": result.get("verbalized_image", ""),
                "url": result.get("metadata_storage_path", ""),
            })
        content_blocks.append(types.TextContent(
            type="text",
            text=json.dumps({"mode": "lightweight", "total_results": len(image_results), "results": image_results}),
        ))
        return content_blocks

    # Full mode: fetch thumbnails
    blob_service_client = get_blob_service_client()

    async with blob_service_client:
        for result in results:
            filename = result.get("metadata_storage_name", "unknown.jpg")
            description = result.get("verbalized_image", "")
            storage_path = result.get("metadata_storage_path", "")

            try:
                blob_client = blob_service_client.get_blob_client(
                    container=BLOB_CONTAINER, blob=filename
                )
                stream = await blob_client.download_blob()
                image_bytes = await stream.readall()

                thumbnail_bytes = resize_image_bytes(image_bytes)
                mime_type = get_mime_type(filename)

                content_blocks.append(types.ImageContent(
                    type="image",
                    data=base64.b64encode(thumbnail_bytes).decode("utf-8"),
                    mimeType=mime_type,
                ))
                image_results.append({
                    "filename": filename,
                    "display_name": filename,
                    "description": description,
                    "storage_path": storage_path,
                })
            except Exception as e:
                image_results.append({
                    "filename": filename,
                    "display_name": filename,
                    "description": description,
                    "error": str(e),
                })

    content_blocks.append(types.TextContent(
        type="text",
        text=json.dumps({"mode": "full", "total_results": len(image_results), "results": image_results}),
    ))
    return content_blocks


@mcp.tool(annotations={"readOnlyHint": True})
async def get_image_details(
    filename: Annotated[str, "The filename of the image to retrieve details for"],
) -> list:
    """
    Get full metadata and a larger preview for a specific image.
    Use after image_search to inspect a particular result more closely.
    """
    blob_service_client = get_blob_service_client()

    async with blob_service_client:
        blob_client = blob_service_client.get_blob_client(
            container=BLOB_CONTAINER, blob=filename
        )
        stream = await blob_client.download_blob()
        image_bytes = await stream.readall()
        properties = await blob_client.get_blob_properties()

    mime_type = get_mime_type(filename)

    with Image.open(io.BytesIO(image_bytes)) as img:
        width, height = img.size
        image_format = img.format or "JPEG"

    detail_bytes = resize_image_bytes(image_bytes, max_size=(1024, 1024))

    return [
        types.ImageContent(
            type="image",
            data=base64.b64encode(detail_bytes).decode("utf-8"),
            mimeType=mime_type,
        ),
        types.TextContent(
            type="text",
            text=json.dumps({
                "filename": filename,
                "mimeType": mime_type,
                "width": width,
                "height": height,
                "format": image_format,
                "sizeBytes": len(image_bytes),
                "contentType": properties.content_settings.content_type,
                "lastModified": properties.last_modified.isoformat() if properties.last_modified else None,
            }),
        ),
    ]


@mcp.tool(annotations={"readOnlyHint": True})
async def display_images(
    filenames: Annotated[list[str], "List of image filenames to display in the viewer"],
    descriptions: Annotated[list[str], "Image descriptions, in same order as filenames"],
) -> list:
    """
    Display selected images in an interactive carousel viewer.
    Use after image_search to present curated results to the user.
    """
    blob_service_client = get_blob_service_client()
    content_blocks: list = []
    image_results: list[dict] = []

    async with blob_service_client:
        for i, filename in enumerate(filenames):
            try:
                blob_client = blob_service_client.get_blob_client(
                    container=BLOB_CONTAINER, blob=filename
                )
                stream = await blob_client.download_blob()
                image_bytes = await stream.readall()
                mime_type = get_mime_type(filename)

                with Image.open(io.BytesIO(image_bytes)) as img:
                    width, height = img.size
                    image_format = img.format or "JPEG"

                content_blocks.append(types.ImageContent(
                    type="image",
                    data=base64.b64encode(image_bytes).decode("utf-8"),
                    mimeType=mime_type,
                ))
                image_results.append({
                    "filename": filename,
                    "description": descriptions[i] if i < len(descriptions) else "",
                    "mimeType": mime_type,
                    "width": width,
                    "height": height,
                    "format": image_format,
                    "sizeBytes": len(image_bytes),
                })
            except Exception as e:
                image_results.append({
                    "filename": filename,
                    "description": descriptions[i] if i < len(descriptions) else "",
                    "error": str(e),
                })

    content_blocks.append(types.TextContent(
        type="text",
        text=json.dumps({"images": image_results}),
    ))
    return content_blocks


# ─── MCP Resource ─────────────────────────────────────────────────────────────


@mcp.resource(IMAGE_VIEW_URI)
def image_viewer() -> str:
    """Render images as an interactive carousel viewer."""
    viewer_path = Path(__file__).parent / "viewer.html"
    return viewer_path.read_text(encoding="utf-8")


# ─── FastAPI REST Layer ────────────────────────────────────────────────────────

app = FastAPI(title="Azure AI Image Search", version="1.0.0")


@app.on_event("startup")
async def startup_event():
    """Validate configuration on startup."""
    logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
    validate_config()
    logger.info(
        f"Server starting — mode={'lightweight' if LIGHTWEIGHT_MODE else 'full'}, "
        f"index={SEARCH_INDEX}"
    )


@app.get("/health")
async def health_check():
    """Health check for ACA liveness/readiness probes."""
    return {
        "status": "healthy",
        "mode": "lightweight" if LIGHTWEIGHT_MODE else "full",
        "search_endpoint": SEARCH_ENDPOINT,
        "index": SEARCH_INDEX,
    }


async def verify_api_key(x_api_key: str = Header(alias="X-API-Key")):
    """Validate API key from request header."""
    if not API_KEY:
        return  # No key configured — allow all (dev mode)
    if x_api_key != API_KEY:
        raise HTTPException(status_code=401, detail="Invalid API key")


class SearchRequest(BaseModel):
    query: str
    max_results: int = 5


class SearchByImageRequest(BaseModel):
    image_url: str | None = None
    image_base64: str | None = None
    max_results: int = 5


class UploadResponse(BaseModel):
    filename: str
    size_bytes: int
    content_type: str
    message: str


@app.post("/api/search", dependencies=[Depends(verify_api_key)])
async def api_search_images(body: SearchRequest):
    """REST endpoint for SearchImages typed operation."""
    search_client = get_search_client()
    results = run_search(search_client, body.query, body.max_results)

    image_results = []

    if LIGHTWEIGHT_MODE:
        for result in results:
            image_results.append({
                "filename": result.get("metadata_storage_name", "unknown.jpg"),
                "display_name": result.get("metadata_storage_name", "unknown.jpg"),
                "description": result.get("verbalized_image", ""),
                "url": result.get("metadata_storage_path", ""),
            })
    else:
        blob_service_client = get_blob_service_client()
        async with blob_service_client:
            for result in results:
                filename = result.get("metadata_storage_name", "unknown.jpg")
                description = result.get("verbalized_image", "")
                mime_type = get_mime_type(filename)
                entry = {
                    "filename": filename,
                    "display_name": filename,
                    "description": description,
                    "mime_type": mime_type,
                }
                try:
                    blob_client = blob_service_client.get_blob_client(
                        container=BLOB_CONTAINER, blob=filename
                    )
                    stream = await blob_client.download_blob()
                    image_bytes = await stream.readall()
                    thumbnail_bytes = resize_image_bytes(image_bytes)
                    entry["thumbnail_base64"] = base64.b64encode(thumbnail_bytes).decode("utf-8")
                except Exception as e:
                    entry["error"] = str(e)
                image_results.append(entry)

    return {
        "query": body.query,
        "total_results": len(image_results),
        "results": image_results,
    }


@app.post("/api/search-by-image", dependencies=[Depends(verify_api_key)])
async def api_search_by_image(body: SearchByImageRequest):
    """REST endpoint for SearchByImage typed operation."""
    if not body.image_url and not body.image_base64:
        raise HTTPException(status_code=400, detail="Provide either image_url or image_base64")

    if not body.image_url:
        raise HTTPException(status_code=400, detail="Reverse image search requires image_url. Base64 input not yet supported.")

    search_client = get_search_client()

    results = search_client.search(
        search_text="",
        top=body.max_results,
        vector_queries=[
            VectorizableImageUrlQuery(
                url=body.image_url,
                k_nearest_neighbors=body.max_results,
                fields="embedding",
            )
        ],
        select=["metadata_storage_path", "metadata_storage_name", "verbalized_image"],
    )

    image_results = []
    for result in results:
        image_results.append({
            "filename": result.get("metadata_storage_name", "unknown.jpg"),
            "display_name": result.get("metadata_storage_name", "unknown.jpg"),
            "description": result.get("verbalized_image", ""),
            "url": result.get("metadata_storage_path", ""),
        })

    return {
        "total_results": len(image_results),
        "results": image_results,
    }


@app.get("/api/images/{filename}", dependencies=[Depends(verify_api_key)])
async def api_get_image_details(filename: str):
    """REST endpoint for GetImageDetails typed operation."""
    blob_service_client = get_blob_service_client()

    async with blob_service_client:
        blob_client = blob_service_client.get_blob_client(
            container=BLOB_CONTAINER, blob=filename
        )
        try:
            stream = await blob_client.download_blob()
            image_bytes = await stream.readall()
            properties = await blob_client.get_blob_properties()
        except Exception:
            raise HTTPException(status_code=404, detail=f"Image not found: {filename}")

    with Image.open(io.BytesIO(image_bytes)) as img:
        width, height = img.size
        image_format = img.format or "JPEG"

    return {
        "filename": filename,
        "mimeType": get_mime_type(filename),
        "width": width,
        "height": height,
        "format": image_format,
        "sizeBytes": len(image_bytes),
        "lastModified": properties.last_modified.isoformat() if properties.last_modified else None,
    }


@app.get("/api/images/{filename}/url", dependencies=[Depends(verify_api_key)])
async def api_get_image_url(filename: str, expiry_minutes: int = 60):
    """REST endpoint for GetImageUrl typed operation. Returns a time-limited SAS URL."""
    if not BLOB_CONNECTION_STRING:
        raise HTTPException(status_code=501, detail="Blob Storage not configured (lightweight mode)")

    # Parse account name and key from connection string
    parts = dict(part.split("=", 1) for part in BLOB_CONNECTION_STRING.split(";") if "=" in part)
    account_name = parts.get("AccountName", "")
    account_key = parts.get("AccountKey", "")

    if not account_name or not account_key:
        raise HTTPException(status_code=500, detail="Cannot generate SAS: invalid connection string")

    expiry = datetime.now(timezone.utc) + timedelta(minutes=expiry_minutes)

    sas_token = generate_blob_sas(
        account_name=account_name,
        container_name=BLOB_CONTAINER,
        blob_name=filename,
        account_key=account_key,
        permission=BlobSasPermissions(read=True),
        expiry=expiry,
    )

    url = f"https://{account_name}.blob.core.windows.net/{BLOB_CONTAINER}/{filename}?{sas_token}"

    return {
        "url": url,
        "filename": filename,
        "expires_at": expiry.isoformat(),
    }


@app.post("/api/upload", dependencies=[Depends(verify_api_key)])
async def api_upload_image(file: UploadFile = FastAPIFile(...)):
    """Upload an image to the collection. Triggers indexing on next indexer run."""
    if not file.content_type or not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="File must be an image")

    if not BLOB_CONNECTION_STRING:
        raise HTTPException(status_code=501, detail="Blob Storage not configured (lightweight mode)")

    image_bytes = await file.read()
    filename = file.filename or "uploaded.jpg"

    blob_service_client = get_blob_service_client()
    async with blob_service_client:
        blob_client = blob_service_client.get_blob_client(
            container=BLOB_CONTAINER, blob=filename
        )
        await blob_client.upload_blob(
            image_bytes,
            overwrite=True,
            content_settings={"content_type": file.content_type},
        )

    return UploadResponse(
        filename=filename,
        size_bytes=len(image_bytes),
        content_type=file.content_type,
        message=f"Uploaded {filename}. It will appear in search results after the next indexer run.",
    )


# ─── Mount MCP on FastAPI ──────────────────────────────────────────────────────

# FastMCP exposes an ASGI app; mount it at /mcp
app.mount("/mcp", mcp.http_app(path="/"))


# ─── Entry Point ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
