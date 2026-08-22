import os
os.environ["NO_PROXY"] = "localhost,127.0.0.1,0.0.0.0"
os.environ["no_proxy"] = "localhost,127.0.0.1,0.0.0.0"

# 2. HuggingFace 强制离线模式（使用本地缓存，不联网）
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"


from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import Optional
from contextlib import asynccontextmanager
import chromadb
from ollama import AsyncClient
from sentence_transformers import SentenceTransformer
import uvicorn
import time
import asyncio
import signal
import sys
import glob
import os
import base64
import httpx
from pydantic import BaseModel as PydanticBaseModel
from typing import List, Optional
import csv
import math
from datetime import datetime, timezone

# =============================================================================
# 全局变量
# =============================================================================
chroma_client = None
collection = None
embedder = None
ollama_client = None
MEMORY_DB_PATH = os.environ.get(
    "VRAG_MEMORY_DB",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "robot_memory_db"),
)


# =============================================================================
# 工具函数
# =============================================================================
def generate_minimal_test_image() -> str:
    """生成 1x1 像素 PNG 的 base64，用于预热视觉模型"""
    minimal_png = bytes([
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,
        0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82
    ])
    return base64.b64encode(minimal_png).decode('utf-8')


# =============================================================================
# Lifespan（替代已弃用的 on_event）
# =============================================================================
@asynccontextmanager
async def lifespan(app: FastAPI):
    global chroma_client, collection, embedder, ollama_client

    print("=" * 60)
    print("  Robot Brain Server — Starting Up...")
    print("=" * 60)

    # --- ChromaDB ---
    print("[Startup] Initializing ChromaDB...")
    try:
        chroma_client = chromadb.PersistentClient(path=MEMORY_DB_PATH)
        collection = chroma_client.get_or_create_collection(name="robot_memories")
        print(f"[Startup] ChromaDB OK. Memory count: {collection.count()}")
    except Exception as e:
        print(f"[Startup] ChromaDB init failed: {e}")
        print("[Startup] Attempting to remove stale lock files...")
        for lock_file in glob.glob(os.path.join(MEMORY_DB_PATH, "*.lock")):
            try:
                os.remove(lock_file)
                print(f"  Removed: {lock_file}")
            except Exception as le:
                print(f"  Failed to remove {lock_file}: {le}")
        try:
            chroma_client = chromadb.PersistentClient(path=MEMORY_DB_PATH)
            collection = chroma_client.get_or_create_collection(name="robot_memories")
            print(f"[Startup] ChromaDB recovered. Memory count: {collection.count()}")
        except Exception as e2:
            print(f"[Startup] ChromaDB FATAL: {e2}. Memory features disabled.")

    # --- Embedding ---
    print("[Startup] Loading Embedding Model (all-MiniLM-L6-v2)...")
    embedder = SentenceTransformer('all-MiniLM-L6-v2')
    print("[Startup] Embedding Model loaded.")

    # --- Ollama 单例客户端 ---
    ollama_client = AsyncClient()

    # --- Ollama 健康检查 ---
    print("[Startup] Checking Ollama connectivity...")
    ollama_ok = False
    try:
        async with httpx.AsyncClient() as client:
            resp = await client.get("http://localhost:11434/api/tags", timeout=5.0)
            if resp.status_code == 200:
                print(f"[Startup] Ollama is running.")
                ollama_ok = True
            else:
                print(f"[Startup] WARNING: Ollama returned status {resp.status_code}")
    except Exception as e:
        print(f"[Startup] WARNING: Cannot reach Ollama — {e}")

    # --- 预热 moondream（★ 必须带图片！） ---
    if ollama_ok:
        print("[Startup] Pre-warming moondream model (this may take a moment)...")
        try:
            test_image = generate_minimal_test_image()
            await asyncio.wait_for(
                ollama_client.chat(
                    model="moondream",
                    messages=[{
                        'role': 'user',
                        'content': 'Describe this image.',
                        'images': [test_image]
                    }],
                ),
                timeout=60.0
            )
            print("[Startup] llava pre-loaded into VRAM. Ready.")
        except asyncio.TimeoutError:
            print("[Startup] WARNING: moondream pre-warm timed out (60s).")
        except Exception as e:
            print(f"[Startup] WARNING: moondream pre-warm failed: {e}")
            print("[Startup] Model will load on first real request.")

    print("=" * 60)
    print("  Robot Brain Server is READY")
    print("=" * 60)

    yield  # ← 应用运行期间在此挂起

    # --- 关闭清理 ---
    print("[Shutdown] Releasing resources...")
    try:
        if chroma_client is not None:
            del chroma_client
            print("[Shutdown] ChromaDB released.")
    except Exception as e:
        print(f"[Shutdown] ChromaDB release error: {e}")
    print("[Shutdown] Done.")


app = FastAPI(lifespan=lifespan)


# =============================================================================
# 数据模型
# =============================================================================
class VisionRequest(BaseModel):
    model_name: str = "llava"
    image_base64: str
    prompt: str = (
        "Describe this image concisely, if you see any bright colored objects, "
        "cubes, blocks, you MUST start your response with 'FOUND:'."
    )
    seed: int = 42
    temperature: float = 0.0


class MemoryAddRequest(BaseModel):
    content: str
    location: str = "unknown"
    timestamp: str = ""
    created_unix: float = 0.0
    memory_id: str = ""
    run_id: str = ""
    observation_id: str = ""
    category: str = "observation"
    source_model: str = "unknown"
    status: str = "accepted"
    credibility: float = 1.0
    raw_credibility: float = 1.0
    position_x: float = 0.0
    position_y: float = 0.0
    position_z: float = 0.0
    confirmation_count: int = 0
    contradiction_count: int = 0


class MemoryQueryRequest(BaseModel):
    query_text: str
    n_results: int = 5
    filter_location: Optional[str] = ""
    retrieval_mode: str = "semantic"
    trust_weight: float = 0.35
    recency_weight: float = 0.10
    spatial_weight: float = 0.10
    min_credibility: float = 0.0
    include_candidates: bool = True
    query_x: float = 0.0
    query_y: float = 0.0
    query_z: float = 0.0


class MemoryStatusUpdateRequest(BaseModel):
    memory_id: str
    status: str
    credibility: float
    confirmation_count: int = 0
    contradiction_count: int = 0
    reason: str = ""


class ExperimentMemoryResetRequest(BaseModel):
    confirm: bool = False
    run_label: str = ""

# =============================================================================
# API 接口
# =============================================================================

@app.get("/")
def read_root():
    mem_count = 0
    try:
        if collection is not None:
            mem_count = collection.count()
    except Exception:
        pass
    return {"status": "Robot Brain Server is Running", "memory_count": mem_count}


@app.post("/experiment/reset-memory")
async def reset_experiment_memory(request: ExperimentMemoryResetRequest):
    """Clear cross-trial robot memories. Call only before starting a trial."""
    global collection, chroma_client
    if not request.confirm:
        raise HTTPException(status_code=400, detail="Set confirm=true to reset experiment memory")
    if chroma_client is None:
        raise HTTPException(status_code=503, detail="ChromaDB is not initialized")

    previous_count = collection.count() if collection is not None else 0
    try:
        try:
            chroma_client.delete_collection(name="robot_memories")
        except Exception as error:
            if "does not exist" not in str(error).lower() and "not found" not in str(error).lower():
                raise
        collection = chroma_client.get_or_create_collection(name="robot_memories")
        print(f"[Experiment] Memory reset: {request.run_label or 'unlabelled'}; removed={previous_count}")
        return {
            "status": "ok",
            "run_label": request.run_label,
            "removed_memories": previous_count,
            "memory_count": collection.count(),
        }
    except Exception as error:
        raise HTTPException(status_code=500, detail=f"Memory reset failed: {error}") from error


@app.get("/health")
async def health_check():
    issues = []

    # 检查 ChromaDB
    try:
        if collection is None:
            issues.append("ChromaDB: not initialized")
        else:
            collection.count()
    except Exception as e:
        issues.append(f"ChromaDB: {str(e)}")

    # 检查 Embedding
    if embedder is None:
        issues.append("Embedder: not loaded")

    # 检查 Ollama — 只检查它是否在运行，不加载模型
    try:
        async with httpx.AsyncClient() as client:
            resp = await client.get("http://localhost:11434/api/tags", timeout=5.0)
            if resp.status_code != 200:
                issues.append(f"Ollama: returned status {resp.status_code}")
    except Exception as e:
        issues.append(f"Ollama: cannot connect — {str(e)}")


    return {
        "status": "ok" if not issues else "degraded",
        "healthy": not issues,
        "issues": issues,
        "memory_count": collection.count() if collection is not None else 0,
        "memory_db_path": MEMORY_DB_PATH,
        "embedding_model": "all-MiniLM-L6-v2",
    }

@app.post("/vision")
async def analyze_image(req: VisionRequest):
    try:
        clean_base64 = req.image_base64
        if "," in clean_base64:
            clean_base64 = clean_base64.split(",")[1]

        img_size_kb = len(clean_base64) * 3 / 4 / 1024
        print(f"[Vision] Image size: {img_size_kb:.1f} KB | Model: {req.model_name}")
        print(f"[Vision] Prompt: {req.prompt[:80]}...")

        # ★ 不用 ollama Python 库，直接用 httpx 调 Ollama HTTP API
        payload = {
            "model": req.model_name,
            "messages": [
                {
                    "role": "user",
                    "content": req.prompt,
                    "images": [clean_base64]
                }
            ],
            "stream": False,
            "options": {
                "num_predict": 300,
                "temperature": max(0.0, min(2.0, req.temperature)),
                "seed": req.seed,
            }
        }

        async with httpx.AsyncClient(proxy=None, timeout=90.0) as client:
            resp = await client.post(
                "http://localhost:11434/api/chat",
                json=payload,
            )

        print(f"[Vision] Ollama HTTP status: {resp.status_code}")

        if resp.status_code != 200:
            print(f"[Vision] Ollama error: {resp.text}")
            raise HTTPException(status_code=resp.status_code, detail=resp.text)

        result = resp.json()
        desc = result.get("message", {}).get("content", "")

        # 打印完整诊断信息
        eval_count = result.get("eval_count", 0)
        total_duration = result.get("total_duration", 0) / 1e9  # 转换为秒
        print(f"[Vision] eval_count: {eval_count} | duration: {total_duration:.1f}s")
        print(f"[Vision] Full response ({len(desc)} chars): '{desc}'")

        if not desc or len(desc.strip()) < 5:
            print("[Vision] WARNING: Empty response!")
            desc = "Unable to describe the scene clearly."

        return {"description": desc}

    except httpx.TimeoutException:
        print("[Vision] ERROR: Timeout (>90s)")
        raise HTTPException(status_code=504, detail="Ollama timeout")
    except HTTPException:
        raise
    except Exception as e:
        print(f"[Vision] ERROR: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


def _clip01(value: float) -> float:
    return max(0.0, min(1.0, float(value)))


def _semantic_score(distance: float) -> float:
    """Monotonic conversion for Chroma's default non-negative distance."""
    return 1.0 / (1.0 + max(0.0, float(distance)))


def _recency_score(created_unix: float, half_life_seconds: float = 3600.0) -> float:
    if created_unix <= 0.0:
        return 0.5
    age = max(0.0, time.time() - created_unix)
    return math.exp(-math.log(2.0) * age / max(1.0, half_life_seconds))


def _spatial_score(meta: dict, req: MemoryQueryRequest, scale_metres: float = 20.0) -> float:
    dx = float(meta.get("position_x", 0.0)) - req.query_x
    dy = float(meta.get("position_y", 0.0)) - req.query_y
    dz = float(meta.get("position_z", 0.0)) - req.query_z
    return math.exp(-math.sqrt(dx * dx + dy * dy + dz * dz) / max(0.1, scale_metres))


@app.post("/memory/add")
def add_memory(req: MemoryAddRequest):
    if collection is None or embedder is None:
        raise HTTPException(status_code=503, detail="Memory system not initialized.")

    try:
        timestamp = req.timestamp or datetime.now(timezone.utc).isoformat()
        created_unix = req.created_unix if req.created_unix > 0.0 else time.time()
        memory_id = req.memory_id.strip() or f"mem_{time.time_ns()}"
        embedding = embedder.encode(req.content, normalize_embeddings=True).tolist()
        metadata = {
            "schema_version": 2,
            "run_id": req.run_id or "legacy",
            "observation_id": req.observation_id or memory_id,
            "location": req.location or "unknown",
            "timestamp": timestamp,
            "created_unix": float(created_unix),
            "category": req.category or "observation",
            "source_model": req.source_model or "unknown",
            "status": req.status or "accepted",
            "credibility": _clip01(req.credibility),
            "raw_credibility": _clip01(req.raw_credibility),
            "position_x": float(req.position_x),
            "position_y": float(req.position_y),
            "position_z": float(req.position_z),
            "confirmation_count": max(0, int(req.confirmation_count)),
            "contradiction_count": max(0, int(req.contradiction_count)),
        }

        collection.add(
            documents=[req.content],
            embeddings=[embedding],
            metadatas=[metadata],
            ids=[memory_id],
        )
        print(f"[Memory Add] {memory_id} state={metadata['status']} C={metadata['credibility']:.3f}")
        return {"status": "success", "id": memory_id}
    except Exception as error:
        print(f"[Memory Add] ERROR: {error}")
        raise HTTPException(status_code=500, detail=str(error)) from error


@app.post("/memory/update-status")
def update_memory_status(req: MemoryStatusUpdateRequest):
    if collection is None:
        raise HTTPException(status_code=503, detail="Memory system not initialized.")
    existing = collection.get(ids=[req.memory_id], include=["metadatas"])
    if not existing.get("ids"):
        raise HTTPException(status_code=404, detail=f"Memory not found: {req.memory_id}")
    metadata = dict(existing["metadatas"][0] or {})
    metadata.update({
        "status": req.status,
        "credibility": _clip01(req.credibility),
        "confirmation_count": max(0, req.confirmation_count),
        "contradiction_count": max(0, req.contradiction_count),
        "status_reason": req.reason or "",
        "status_updated_unix": time.time(),
    })
    collection.update(ids=[req.memory_id], metadatas=[metadata])
    return {"status": "success", "id": req.memory_id, "memory_status": req.status}


@app.post("/memory/query")
def query_memory(req: MemoryQueryRequest):
    if collection is None or embedder is None or collection.count() == 0:
        return {"results": []}

    try:
        mode = (req.retrieval_mode or "semantic").strip().lower()
        query_embedding = embedder.encode(req.query_text, normalize_embeddings=True).tolist()
        where_clause = None
        if req.filter_location and req.filter_location.strip():
            where_clause = {"location": req.filter_location.strip()}

        actual_count = collection.count()
        final_count = max(1, min(req.n_results, actual_count))
        pool_count = min(actual_count, max(final_count, final_count * 5))
        results = collection.query(
            query_embeddings=[query_embedding],
            n_results=pool_count,
            where=where_clause,
            include=["documents", "metadatas", "distances"],
        )

        documents = results.get("documents", [[]])[0]
        metadatas = results.get("metadatas", [[]])[0]
        distances = results.get("distances", [[]])[0]
        ids = results.get("ids", [[]])[0]
        ranked = []
        for memory_id, document, metadata, distance in zip(ids, documents, metadatas, distances):
            meta = metadata or {}
            status = str(meta.get("status", "accepted"))
            credibility = _clip01(meta.get("credibility", 1.0))

            if mode in {"heuristic_trust", "reliability_aware"}:
                if mode == "reliability_aware":
                    if status in {"quarantined", "contradicted", "rejected"}:
                        continue
                    if status == "candidate" and not req.include_candidates:
                        continue
                if credibility < _clip01(req.min_credibility):
                    continue

            semantic = _semantic_score(distance)
            recency = _recency_score(float(meta.get("created_unix", 0.0)))
            spatial = _spatial_score(meta, req)
            state_factor = {
                "verified": 1.0,
                "accepted": 0.9,
                "candidate": 0.4,
            }.get(status, 0.5)
            trust = credibility * state_factor

            if mode == "heuristic_trust":
                final_score = 0.6 * semantic + 0.4 * credibility
            elif mode == "reliability_aware":
                tw = max(0.0, req.trust_weight)
                rw = max(0.0, req.recency_weight)
                sw = max(0.0, req.spatial_weight)
                denominator = 1.0 + tw + rw + sw
                final_score = (semantic + tw * trust + rw * recency + sw * spatial) / denominator
            else:
                final_score = semantic


            ranked.append({
                "memory_id": memory_id,
                "content": document,
                "location": meta.get("location", "unknown"),
                "timestamp": meta.get("timestamp", "unknown"),
                "category": meta.get("category", "unknown"),
                "source_model": meta.get("source_model", "unknown"),
                "status": status,
                "credibility": credibility,
                "raw_credibility": _clip01(meta.get("raw_credibility", credibility)),
                "distance": float(distance),
                "semantic_score": semantic,
                "trust_score": trust,
                "recency_score": recency,
                "spatial_score": spatial,
                "final_score": final_score,
            })

        ranked.sort(key=lambda item: item["final_score"], reverse=True)
        selected = ranked[:final_count]
        for rank, item in enumerate(selected, 1):
            item["rank"] = rank
        print(f"[Memory Query] mode={mode} pool={pool_count} returned={len(selected)}")
        return {"results": selected}
    except Exception as error:
        print(f"[Memory Query] ERROR: {error}")
        raise HTTPException(status_code=500, detail=str(error)) from error

class ExperimentResult(PydanticBaseModel):
    class Config:
        extra = "allow"

    method: str
    scene: str
    trial_id: int
    total_time: float
    path_length: float
    treasures_found: int
    decoys_correctly_rejected: int = 0
    decoys_falsely_accepted: int = 0
    hider_found: bool = False
    hider_find_time: float = 0.0
    llm_calls: int = 0
    vlm_calls: int = 0
    total_observations: int = 0
    hallucination_count: int = 0

experiment_results: List[dict] = []

@app.post("/experiment/submit")
async def submit_experiment(result: ExperimentResult):
    """接收 Unity 提交的实验结果"""
    data = result.dict()
    experiment_results.append(data)

    # 追加到 CSV（论文直接用）
    csv_file = "experiment_results.csv"
    file_exists = os.path.exists(csv_file)

    with open(csv_file, "a", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=data.keys())
        if not file_exists:
            writer.writeheader()
        writer.writerow(data)

    print(f"[Experiment] Saved: {result.method}/{result.scene}/#{result.trial_id}")
    return {"status": "ok", "total_results": len(experiment_results)}


@app.get("/experiment/results")
async def get_all_results():
    return {"results": experiment_results}


@app.get("/experiment/summary")
async def get_summary():
    """生成实验摘要"""
    if not experiment_results:
        return {"message": "No results yet"}

    from collections import defaultdict

    groups = defaultdict(list)
    for r in experiment_results:
        key = f"{r['method']}_{r['scene']}"
        groups[key].append(r)

    summary = {}
    for key, trials in groups.items():
        n = len(trials)
        summary[key] = {
            "trials": n,
            "avg_time": sum(t["total_time"] for t in trials) / n,
            "avg_treasures": sum(t["treasures_found"] for t in trials) / n,
            "avg_path": sum(t["path_length"] for t in trials) / n,
            "avg_hallucination": sum(t["hallucination_count"] for t in trials) / n,
            "completion_rate": sum(
                1 for t in trials if t["treasures_found"] >= 3
            ) / n,
        }

    return summary
# =============================================================================
# 信号处理
# =============================================================================
def graceful_shutdown_handler(signum, frame):
    print(f"\n[Signal] Received signal {signum}, shutting down...")
    try:
        global chroma_client
        if chroma_client is not None:
            del chroma_client
    except Exception:
        pass
    sys.exit(0)


signal.signal(signal.SIGINT, graceful_shutdown_handler)
signal.signal(signal.SIGTERM, graceful_shutdown_handler)


# =============================================================================
# 启动入口
# =============================================================================
if __name__ == "__main__":
    print("Make sure Ollama is running: ollama serve")
    uvicorn.run(app, host="0.0.0.0", port=8000)
