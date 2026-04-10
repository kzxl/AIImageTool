import io
import uuid
import threading
import uvicorn
from fastapi import FastAPI, UploadFile, File
from fastapi.responses import Response, JSONResponse
from PIL import Image
from aura_sr import AuraSR
import torch
import multiprocessing

app = FastAPI(title="AuraSR Upscale Worker")

# Thư mục trỏ tới model offline (Phải trỏ thẳng vào file safetensors)
MODEL_PATH = "./model_offline/model.safetensors"
aura_model = None

# Trạm đệm giữ hình ảnh
job_queue = {}

@app.on_event("startup")
def load_model():
    global aura_model
    try:
        # Vá lỗi (Monkey Patch) thư viện vã cứng cuda
        import aura_sr
        original_init = aura_sr.AuraSR.__init__
        def patched_init(self, config, device="cpu"):
            original_init(self, config, device=device)
        aura_sr.AuraSR.__init__ = patched_init
        
        print(f"[*] Đang tải mô hình AuraSR từ thư mục: {MODEL_PATH}...")
        aura_model = AuraSR.from_pretrained(MODEL_PATH)
        
        # Tự động đẩy model sang GPU (VRAM) nếu máy có Card rời NVIDIA
        device = "cuda" if torch.cuda.is_available() else "cpu"
        aura_model.upsampler.to(device)
        print(f"[*] Đã tải mô hình thành công! Đang chạy bằng sức mạnh của: {device.upper()}")
        
        if device == "cpu":
            print("[!] CẢNH BÁO: Máy không có GPU NVIDIA CUDA, model sẽ chạy bằng CPU (rất chậm và ngốn CPU).")
            # Giới hạn số luồng CPU để máy không bị đơ 100%
            total_cores = multiprocessing.cpu_count()
            safe_threads = max(1, total_cores - 2) # Chừa lại 2 core cho Windows và C#
            torch.set_num_threads(safe_threads)
            print(f"[!] Đã giới hạn PyTorch dùng {safe_threads}/{total_cores} luồng CPU để chống treo máy.")
    except Exception as e:
        print(f"[!] Lỗi khi tải mô hình (Hệ thống vẫn chạy để đợi bạn copy model vào): {e}")

# --- KIẾN TRÚC JOB POLLING BẤT ĐỒNG BỘ ---

def process_upscale_job(job_id: str, image_bytes: bytes):
    try:
        print(f"[*] [Job {job_id[:6]}] Đang đọc ảnh...")
        image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        print(f"[*] [Job {job_id[:6]}] Kích thước {image.size}, đắp pixel (Upscale)...")
        
        # Tiền hành Upscale
        upscaled_image = aura_model.upscale_4x_overlapped(image)
        print(f"[*] [Job {job_id[:6]}] Đã xong! Kích thước vọt lên: {upscaled_image.size}")
        
        out_io = io.BytesIO()
        upscaled_image.save(out_io, format="PNG")
        
        # Cập nhật kết quả vào RAM
        job_queue[job_id] = {"status": "done", "data": out_io.getvalue()}
    except Exception as e:
        print(f"[!] [Job {job_id[:6]}] Lỗi: {str(e)}")
        job_queue[job_id] = {"status": "error", "message": str(e)}

@app.post("/upscale")
async def upscale_queue(file: UploadFile = File(...)):
    if aura_model is None:
        return JSONResponse(status_code=503, content={"error": "Model chưa sẵn sàng"})
    
    contents = await file.read()
    job_id = str(uuid.uuid4())
    job_queue[job_id] = {"status": "processing"}
    
    # Ném sang luồng phụ chạy ngầm
    thread = threading.Thread(target=process_upscale_job, args=(job_id, contents))
    thread.start()
    
    return {"job_id": job_id}

@app.get("/status/{job_id}")
async def check_status(job_id: str):
    if job_id not in job_queue:
        return JSONResponse(status_code=404, content={"error": "Không tìm thấy Job này"})
    
    job = job_queue[job_id]
    
    if job["status"] == "processing":
        return {"status": "processing"}
        
    elif job["status"] == "error":
        err_msg = job["message"]
        del job_queue[job_id]   # Xoá rác RAM
        return JSONResponse(status_code=500, content={"error": err_msg})
        
    elif job["status"] == "done":
        image_bytes = job["data"]
        del job_queue[job_id]   # Xoá rác RAM ngay lập tức sau khi giao hàng
        return Response(content=image_bytes, media_type="image/png")

if __name__ == "__main__":
    # Để C# kết nối qua cổng 8000
    uvicorn.run("main:app", host="127.0.0.1", port=8000, reload=False)
