import base64
import gc
import io
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


# =====================================================
# CONFIGURACION
# =====================================================

# Aqui puedes poner tu api key para pruebas, revisa la seguridad de tus pruebas
API_KEY = os.getenv("GOOGLE_VISION_API_KEY", "")

VISION_URL = "https://vision.googleapis.com/v1/images:annotate"
FEATURES = [{"type": "OBJECT_LOCALIZATION", "maxResults": 20}]

OUTPUT_DIR = Path(__file__).resolve().parent
PHOTO_DIR = OUTPUT_DIR / "Fotos"
RAW_PHOTO_DIR = PHOTO_DIR / "Originales"
DETECTED_PHOTO_DIR = PHOTO_DIR / "Detectadas"
UNITY_JSON_DIR = OUTPUT_DIR / "JSONUnity"
OPENNI2_REDIST = os.getenv("OPENNI2_REDIST", "").strip()
ASTRA_BACKEND = os.getenv("ASTRA_BACKEND", "auto").strip().lower()
ASTRA_COLOR_DEVICE_INDEX = os.getenv("ASTRA_COLOR_DEVICE_INDEX", "").strip()
ASTRA_COLOR_NAME_HINTS = [
    hint.strip().lower()
    for hint in os.getenv("ASTRA_COLOR_NAME_HINTS", "astra,orbbec,usb camera").split(",")
    if hint.strip()
]
ASTRA_WARMUP_FRAMES = int(os.getenv("ASTRA_WARMUP_FRAMES", "30"))
ASTRA_CAPTURE_TIMEOUT_MS = int(os.getenv("ASTRA_CAPTURE_TIMEOUT_MS", "4000"))
MIN_VALID_DEPTH_MM = int(os.getenv("ASTRA_MIN_DEPTH_MM", "150"))
MAX_VALID_DEPTH_MM = int(os.getenv("ASTRA_MAX_DEPTH_MM", "10000"))
MAX_OBJECT_DEPTH_MM = int(os.getenv("ASTRA_MAX_OBJECT_DEPTH_MM", "2500"))
MIN_DEPTH_VALID_RATIO = float(os.getenv("ASTRA_MIN_DEPTH_VALID_RATIO", "0.35"))
MIN_DEPTH_VALID_POINTS = int(os.getenv("ASTRA_MIN_DEPTH_VALID_POINTS", "24"))
MAX_DEPTH_WINDOW_SPREAD_MM = int(os.getenv("ASTRA_MAX_DEPTH_WINDOW_SPREAD_MM", "450"))
VISION_TIMEOUT_SECONDS = int(os.getenv("VISION_TIMEOUT_SECONDS", "20"))
VISION_RETRIES = int(os.getenv("VISION_RETRIES", "3"))


# =====================================================


@dataclass
class CaptureResult:
    color_rgb: np.ndarray
    depth_mm: np.ndarray
    backend: str


def log_step(message):
    print(message, flush=True)


def now_tag():
    return datetime.now().strftime("%Y%m%d_%H%M%S")


def load_font():
    for font_name in ("arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(font_name, 18)
        except OSError:
            continue
    return ImageFont.load_default()


def encode_rgb_image(image_rgb):
    buffer = io.BytesIO()
    Image.fromarray(image_rgb).save(buffer, format="JPEG", quality=95)
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def call_vision(img64):
    payload = {
        "requests": [
            {
                "image": {"content": img64},
                "features": FEATURES,
            }
        ]
    }

    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url=f"{VISION_URL}?key={API_KEY}",
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    last_error = None
    for attempt in range(1, VISION_RETRIES + 1):
        log_step(f"Llamando a Google Vision (intento {attempt}/{VISION_RETRIES})...")
        try:
            with urllib.request.urlopen(req, timeout=VISION_TIMEOUT_SECONDS) as response:
                body = response.read().decode("utf-8")
                log_step(f"Google Vision respondio con HTTP {response.status}.")
                return json.loads(body)
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"Google Vision respondio con error HTTP {exc.code}: {body}"
            ) from exc
        except urllib.error.URLError as exc:
            last_error = exc
            if attempt < VISION_RETRIES:
                log_step(f"Fallo de red al conectar con Google Vision: {exc}. Reintentando...")
                time.sleep(1)
            else:
                break

    raise RuntimeError(
        "No se pudo conectar a Google Vision despues de varios intentos.\n"
        f"Error de red final: {last_error}\n"
        "Revisa tu conexion, firewall, proxy o si la API esta bloqueada en tu red."
    ) from last_error


def clamp(value, low, high):
    return max(low, min(high, value))


def resize_depth_nearest(depth_map, target_h, target_w):
    src_h, src_w = depth_map.shape[:2]
    if (src_h, src_w) == (target_h, target_w):
        return depth_map

    y_idx = np.clip((np.arange(target_h) * src_h / target_h).astype(int), 0, src_h - 1)
    x_idx = np.clip((np.arange(target_w) * src_w / target_w).astype(int), 0, src_w - 1)
    return depth_map[y_idx[:, None], x_idx[None, :]]


def normalized_vertices_to_box(vertices, width, height):
    if not vertices:
        return None

    xs = [v.get("x", 0.0) * width for v in vertices]
    ys = [v.get("y", 0.0) * height for v in vertices]

    x1 = clamp(int(min(xs)), 0, max(width - 1, 0))
    y1 = clamp(int(min(ys)), 0, max(height - 1, 0))
    x2 = clamp(int(max(xs)), 0, max(width - 1, 0))
    y2 = clamp(int(max(ys)), 0, max(height - 1, 0))

    if x2 <= x1:
        x2 = clamp(x1 + 1, 0, width)
    if y2 <= y1:
        y2 = clamp(y1 + 1, 0, height)

    return x1, y1, x2, y2


def frame_size(frame):
    width = frame.get_width() if hasattr(frame, "get_width") else frame.width
    height = frame.get_height() if hasattr(frame, "get_height") else frame.height
    return width, height


def estimate_depth_mm(depth_map_mm, box):
    x1, y1, x2, y2 = box
    if x2 <= x1 or y2 <= y1:
        return None

    box_w = x2 - x1
    box_h = y2 - y1

    max_depth_mm = min(MAX_VALID_DEPTH_MM, MAX_OBJECT_DEPTH_MM)

    def estimate_window(anchor_x, anchor_y, window_ratio):
        roi_w = max(1, int(box_w * window_ratio))
        roi_h = max(1, int(box_h * window_ratio))

        center_x = x1 + int(box_w * anchor_x)
        center_y = y1 + int(box_h * anchor_y)
        rx1 = max(x1, center_x - roi_w // 2)
        ry1 = max(y1, center_y - roi_h // 2)
        rx2 = min(x2, rx1 + roi_w)
        ry2 = min(y2, ry1 + roi_h)

        if rx2 <= rx1 or ry2 <= ry1:
            return None

        roi = depth_map_mm[ry1:ry2, rx1:rx2]
        valid = roi[(roi >= MIN_VALID_DEPTH_MM) & (roi <= max_depth_mm)]
        valid_ratio = valid.size / max(1, roi.size)
        if valid.size < MIN_DEPTH_VALID_POINTS or valid_ratio < MIN_DEPTH_VALID_RATIO:
            return None

        p25, p50, p75 = np.percentile(valid, [25, 50, 75])
        if (p75 - p25) > MAX_DEPTH_WINDOW_SPREAD_MM:
            return None

        near_values = valid[valid <= p50]
        if near_values.size < MIN_DEPTH_VALID_POINTS:
            return None

        return int(np.median(near_values))

    # Pedimos consenso entre varias subventanas del objeto. Si solo una "ve" algo
    # y las demas no, preferimos devolver sin profundidad en lugar de quedarnos con fondo.
    for window_ratio in (0.08, 0.12, 0.18):
        candidate_depths = []
        for anchor_x, anchor_y in (
            (0.50, 0.50),
            (0.38, 0.50),
            (0.62, 0.50),
            (0.50, 0.38),
            (0.50, 0.62),
        ):
            depth_value = estimate_window(anchor_x, anchor_y, window_ratio)
            if depth_value is not None:
                candidate_depths.append(depth_value)

        if len(candidate_depths) < 2:
            continue

        candidate_depths.sort()
        if candidate_depths[-1] - candidate_depths[0] > MAX_DEPTH_WINDOW_SPREAD_MM:
            continue

        return int(np.median(candidate_depths))

    return None


def format_depth(depth_mm):
    if depth_mm is None:
        return "sin profundidad"
    if depth_mm >= 1000:
        return f"{depth_mm / 1000:.2f} m"
    return f"{depth_mm} mm"


def text_bbox(draw, position, text, font):
    if hasattr(draw, "textbbox"):
        return draw.textbbox(position, text, font=font)

    width, height = draw.textsize(text, font=font)
    x, y = position
    return x, y, x + width, y + height


def draw_label(draw, image_size, box, text, font):
    image_w, image_h = image_size
    x1, y1, _, _ = box
    pad = 4
    probe_left, probe_top, probe_right, probe_bottom = text_bbox(draw, (0, 0), text, font)
    text_w = probe_right - probe_left
    text_h = probe_bottom - probe_top

    text_x = min(max(pad, x1), max(pad, image_w - text_w - pad))
    preferred_y = y1 - text_h - (pad * 2)
    if preferred_y >= pad:
        text_y = preferred_y
    else:
        text_y = min(max(pad, y1 + pad), max(pad, image_h - text_h - pad))

    left, top, right, bottom = text_bbox(draw, (text_x, text_y), text, font)
    draw.rectangle(
        [left - pad, top - pad, right + pad, bottom + pad],
        fill=(0, 0, 0),
        outline="lime",
        width=1,
    )
    draw.text((text_x, text_y), text, fill="lime", font=font)


def prefab_key_from_name(name):
    cleaned = []
    previous_was_sep = False
    for char in name.lower():
        if char.isalnum():
            cleaned.append(char)
            previous_was_sep = False
        elif not previous_was_sep:
            cleaned.append("_")
            previous_was_sep = True

    prefab_key = "".join(cleaned).strip("_")
    return prefab_key or "objeto"


def read_env_float(name):
    raw_value = os.getenv(name, "").strip()
    if not raw_value:
        return None
    return float(raw_value)


def build_camera_intrinsics(width, height):
    fx = read_env_float("ASTRA_COLOR_FX")
    fy = read_env_float("ASTRA_COLOR_FY")
    cx = read_env_float("ASTRA_COLOR_CX")
    cy = read_env_float("ASTRA_COLOR_CY")

    if fx is not None and fy is not None:
        return {
            "fx": fx,
            "fy": fy,
            "cx": width / 2.0 if cx is None else cx,
            "cy": height / 2.0 if cy is None else cy,
            "source": "env_intrinsics",
        }

    return {
        "fx": float(width),
        "fy": float(height),
        "cx": width / 2.0,
        "cy": height / 2.0,
        "source": "approx_image_size",
    }


def camera_point_from_pixel(center_pixel, depth_mm, intrinsics):
    if depth_mm is None:
        return None

    pixel_x, pixel_y = center_pixel
    z_mm = float(depth_mm)
    x_mm = ((pixel_x - intrinsics["cx"]) * z_mm) / intrinsics["fx"]
    y_mm = ((pixel_y - intrinsics["cy"]) * z_mm) / intrinsics["fy"]
    return {
        "x": round(x_mm, 2),
        "y": round(y_mm, 2),
        "z": round(z_mm, 2),
    }


def unity_point_from_camera(camera_point_mm):
    if camera_point_mm is None:
        return None

    return {
        "x": round(camera_point_mm["x"] / 1000.0, 4),
        "y": round(-camera_point_mm["y"] / 1000.0, 4),
        "z": round(camera_point_mm["z"] / 1000.0, 4),
    }


def relative_output_path(path):
    try:
        return path.relative_to(OUTPUT_DIR).as_posix()
    except ValueError:
        return str(path)


def build_detection_records(color_rgb, depth_mm, response):
    height, width = color_rgb.shape[:2]
    if depth_mm.shape != (height, width):
        depth_mm = resize_depth_nearest(depth_mm, height, width)

    objects = response.get("localizedObjectAnnotations", [])
    detection_records = []

    for index, detected in enumerate(objects, start=1):
        box = normalized_vertices_to_box(
            detected.get("boundingPoly", {}).get("normalizedVertices", []),
            width,
            height,
        )
        if box is None:
            continue

        name = detected.get("name", "objeto")
        score = float(detected.get("score", 0.0))
        center_pixel = (
            int((box[0] + box[2]) / 2),
            int((box[1] + box[3]) / 2),
        )
        detection_records.append(
            {
                "id": index,
                "name": name,
                "prefab_key": prefab_key_from_name(name),
                "score": score,
                "box": box,
                "center_pixel": center_pixel,
                "depth_mm": estimate_depth_mm(depth_mm, box),
            }
        )

    return detection_records


def annotate_capture(color_rgb, detection_records, output_path):
    height, width = color_rgb.shape[:2]
    image = Image.fromarray(color_rgb)
    draw = ImageDraw.Draw(image)
    font = load_font()

    if not detection_records:
        draw_label(draw, (width, height), (12, 32, 12, 32), "Sin objetos detectados", font)

    for detected in detection_records:
        box = detected["box"]
        name = detected["name"]
        score = detected["score"]
        depth_value = detected["depth_mm"]
        label = f"{name} {score:.2f} | {format_depth(depth_value)}"

        draw.rectangle(box, outline="lime", width=4)
        draw_label(draw, (width, height), box, label, font)

    image.save(output_path, quality=95)


def build_unity_json(capture, detection_records, raw_path, annotated_path, vision_json_path):
    height, width = capture.color_rgb.shape[:2]
    intrinsics = build_camera_intrinsics(width, height)
    objects = []

    for detected in detection_records:
        camera_point_mm = camera_point_from_pixel(
            detected["center_pixel"],
            detected["depth_mm"],
            intrinsics,
        )
        unity_point_m = unity_point_from_camera(camera_point_mm)
        x1, y1, x2, y2 = detected["box"]
        center_x, center_y = detected["center_pixel"]

        objects.append(
            {
                "id": detected["id"],
                "name": detected["name"],
                "prefab_key": detected["prefab_key"],
                "score": round(detected["score"], 4),
                "depth_mm": detected["depth_mm"],
                "bbox_pixels": {
                    "x1": x1,
                    "y1": y1,
                    "x2": x2,
                    "y2": y2,
                },
                "center_pixel": {
                    "x": center_x,
                    "y": center_y,
                },
                "position_camera_mm": camera_point_mm,
                "position_unity_m": unity_point_m,
            }
        )

    return {
        "format": "astra_unity_scene_v1",
        "generated_at": datetime.now().isoformat(),
        "backend": capture.backend,
        "camera": {
            "image_width": width,
            "image_height": height,
            "depth_units": "mm",
            "intrinsics": intrinsics,
            "unity_axis_hint": {
                "x": "right",
                "y": "up",
                "z": "forward",
            },
        },
        "files": {
            "raw_image": relative_output_path(raw_path),
            "annotated_image": relative_output_path(annotated_path),
            "vision_json": relative_output_path(vision_json_path),
        },
        "objects": objects,
    }


def save_raw_capture(color_rgb, output_path):
    Image.fromarray(color_rgb).save(output_path, quality=95)


def save_json(data, output_path):
    output_path.write_text(
        json.dumps(data, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def ensure_output_dirs():
    RAW_PHOTO_DIR.mkdir(parents=True, exist_ok=True)
    DETECTED_PHOTO_DIR.mkdir(parents=True, exist_ok=True)
    UNITY_JSON_DIR.mkdir(parents=True, exist_ok=True)


def import_openni_modules():
    try:
        from openni import _openni2 as c_api
        from openni import openni2

        return openni2, c_api, "openni"
    except ImportError:
        try:
            from primesense import _openni2 as c_api
            from primesense import openni2

            return openni2, c_api, "primesense"
        except ImportError as exc:
            raise RuntimeError(
                "No encontre bindings de OpenNI para Python. "
                "Instala primero el OpenNI SDK de Orbbec y luego prueba "
                "'python -m pip install openni'. "
                "Si ese paquete no te funciona en tu entorno, intenta "
                "'python -m pip install primesense'."
            ) from exc


def import_cv2():
    try:
        import cv2

        return cv2
    except ImportError as exc:
        raise RuntimeError(
            "Se necesita opencv-python para convertir o capturar color con este backend."
        ) from exc


def import_pygrabber():
    try:
        from pygrabber.dshow_graph import FilterGraph

        return FilterGraph
    except ImportError as exc:
        raise RuntimeError(
            "Falta instalar 'pygrabber' para identificar por nombre la camara UVC de la Astra."
        ) from exc


def redist_candidate_paths():
    candidates = []

    if OPENNI2_REDIST:
        candidates.append(Path(OPENNI2_REDIST))

    candidates.extend(
        [
            OUTPUT_DIR,
            OUTPUT_DIR / "Redist",
            OUTPUT_DIR / "OpenNI2" / "Redist",
            OUTPUT_DIR / "OpenNI2" / "Tools" / "OpenNI2" / "Redist",
        ]
    )

    for pattern in ("*OpenNI*", "*Orbbec*"):
        for path in OUTPUT_DIR.glob(pattern):
            if not path.is_dir():
                continue
            candidates.extend(
                [
                    path,
                    path / "Redist",
                    path / "OpenNI2" / "Redist",
                    path / "Tools" / "OpenNI2" / "Redist",
                    path / "samples" / "bin",
                ]
            )

    user_profile = os.getenv("USERPROFILE", "").strip()
    if user_profile:
        desktop_candidates = []

        for env_name in ("OneDriveCommercial", "OneDrive", "OneDriveConsumer"):
            base_dir = os.getenv(env_name, "").strip()
            if base_dir:
                desktop_candidates.append(Path(base_dir) / "Desktop")

        desktop_candidates.append(Path(user_profile) / "Desktop")

        for base_dir in desktop_candidates:
            if not base_dir.exists():
                continue
            candidates.append(base_dir)
            for pattern in ("*OpenNI*", "*Orbbec*"):
                for path in base_dir.glob(pattern):
                    if not path.is_dir():
                        continue
                    candidates.extend(
                        [
                            path,
                            path / "Redist",
                            path / "OpenNI2" / "Redist",
                            path / "Tools" / "OpenNI2" / "Redist",
                            path / "samples" / "bin",
                        ]
                    )

    for env_name in ("ProgramFiles", "ProgramFiles(x86)"):
        base_dir = os.getenv(env_name, "").strip()
        if not base_dir:
            continue
        base_path = Path(base_dir)
        candidates.extend(
            [
                base_path / "OpenNI2" / "Redist",
                base_path / "Orbbec" / "OpenNI2" / "Redist",
                base_path / "Orbbec Astra" / "OpenNI2" / "Redist",
            ]
        )

    unique_candidates = []
    seen = set()
    for path in candidates:
        try:
            normalized = str(path.resolve(strict=False)).lower()
        except Exception:
            normalized = str(path).lower()
        if normalized in seen:
            continue
        seen.add(normalized)
        unique_candidates.append(path)

    return unique_candidates


def detect_openni_redist():
    for candidate in redist_candidate_paths():
        if (candidate / "OpenNI2.dll").exists():
            return candidate
    return None


def list_uvc_cameras():
    FilterGraph = import_pygrabber()
    graph = FilterGraph()
    return list(enumerate(graph.get_input_devices()))


def choose_astra_camera():
    if ASTRA_COLOR_DEVICE_INDEX:
        return int(ASTRA_COLOR_DEVICE_INDEX), "forzado por ASTRA_COLOR_DEVICE_INDEX"

    cameras = list_uvc_cameras()
    for index, name in cameras:
        normalized_name = name.lower()
        if any(hint in normalized_name for hint in ASTRA_COLOR_NAME_HINTS):
            return index, name

    non_microsoft = [
        (index, name) for index, name in cameras if "microsoft" not in name.lower()
    ]
    if len(non_microsoft) == 1:
        index, name = non_microsoft[0]
        return index, f"{name} (detectada automaticamente)"

    available = ", ".join(f"{index}:{name}" for index, name in cameras) or "ninguna"
    raise RuntimeError(
        "No pude identificar automaticamente la camara UVC de la Astra.\n"
        "Usa ASTRA_COLOR_DEVICE_INDEX para forzar el indice correcto.\n"
        f"Camaras disponibles: {available}"
    )


def print_uvc_cameras():
    cameras = list_uvc_cameras()
    if not cameras:
        print("No se detectaron camaras UVC.")
        return

    print("Camaras detectadas:")
    for index, name in cameras:
        print(f"  {index}: {name}")


def capture_color_with_uvc():
    errors = []

    for camera_index, camera_name in candidate_uvc_cameras():
        try:
            return capture_color_with_uvc_subprocess(camera_index, camera_name)
        except Exception as exc:
            errors.append(f"{camera_index}:{camera_name}: {exc}")

    joined = "\n".join(f"- {error}" for error in errors) or "- no encontre indices UVC para probar"
    raise RuntimeError(
        "No pude capturar color desde la camara UVC de la Astra.\n"
        f"Detalles:\n{joined}"
    )


def open_uvc_camera():
    cv2 = import_cv2()
    camera_index, camera_name = choose_astra_camera()
    video_capture = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)

    if not video_capture.isOpened():
        raise RuntimeError(
            f"No pude abrir la camara UVC de la Astra en el indice {camera_index} ({camera_name})."
        )

    video_capture.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    video_capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    video_capture.set(cv2.CAP_PROP_FPS, 30)
    return video_capture, camera_name


def candidate_uvc_cameras():
    candidates = []
    seen = set()

    def add_candidate(index, name):
        key = int(index)
        if key in seen:
            return
        seen.add(key)
        candidates.append((key, name))

    if ASTRA_COLOR_DEVICE_INDEX:
        add_candidate(int(ASTRA_COLOR_DEVICE_INDEX), "forzado por ASTRA_COLOR_DEVICE_INDEX")
        return candidates

    try:
        cameras = list_uvc_cameras()
    except Exception:
        cameras = []

    hinted = []
    non_microsoft = []
    for index, name in cameras:
        normalized_name = name.lower()
        if any(hint in normalized_name for hint in ASTRA_COLOR_NAME_HINTS):
            hinted.append((index, name))
        if "microsoft" not in normalized_name:
            non_microsoft.append((index, name))

    for index, name in hinted:
        add_candidate(index, name)
    for index, name in non_microsoft:
        add_candidate(index, name)
    for index, name in cameras:
        add_candidate(index, name)

    for index in range(6):
        add_candidate(index, f"indice {index}")

    return candidates


def capture_color_with_uvc_subprocess(camera_index, camera_name):
    capture_code = """
import base64
import sys

import cv2

index = int(sys.argv[1])
warmup = int(sys.argv[2])

backends = []
cap_dshow = getattr(cv2, "CAP_DSHOW", None)
if cap_dshow is not None:
    backends.append(("dshow", cap_dshow))
cap_msmf = getattr(cv2, "CAP_MSMF", None)
if cap_msmf is not None:
    backends.append(("msmf", cap_msmf))
backends.append(("default", None))

last_error = None
for backend_name, backend_value in backends:
    cap = cv2.VideoCapture(index, backend_value) if backend_value is not None else cv2.VideoCapture(index)
    try:
        if not cap.isOpened():
            last_error = f"{backend_name}: no abrio"
            continue

        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        cap.set(cv2.CAP_PROP_FPS, 30)

        last_frame = None
        for _ in range(warmup):
            ok, frame = cap.read()
            if ok and frame is not None:
                last_frame = frame

        if last_frame is None:
            last_error = f"{backend_name}: no devolvio frame"
            continue

        ok, encoded = cv2.imencode(".jpg", last_frame, [int(cv2.IMWRITE_JPEG_QUALITY), 95])
        if not ok:
            last_error = f"{backend_name}: no pudo codificar jpg"
            continue

        sys.stdout.write(base64.b64encode(encoded.tobytes()).decode("ascii"))
        sys.exit(0)
    finally:
        cap.release()

raise RuntimeError(last_error or "sin backend valido")
"""
    completed = subprocess.run(
        [sys.executable, "-c", capture_code, str(camera_index), str(ASTRA_WARMUP_FRAMES)],
        capture_output=True,
        text=True,
        timeout=max(15, ASTRA_CAPTURE_TIMEOUT_MS // 1000 + 10),
        check=False,
    )

    if completed.returncode != 0 or not completed.stdout.strip():
        error_text = completed.stderr.strip() or completed.stdout.strip() or f"codigo {completed.returncode}"
        raise RuntimeError(error_text)

    jpeg_bytes = base64.b64decode(completed.stdout.strip())
    image = Image.open(io.BytesIO(jpeg_bytes)).convert("RGB")
    return np.array(image), f"UVC ({camera_name})"


def openni_color_stream_to_rgb(frame):
    width, height = frame_size(frame)
    data = np.frombuffer(frame.get_buffer_as_uint8(), dtype=np.uint8)
    return data.reshape((height, width, 3)).copy()


def openni_depth_stream_to_mm(frame):
    width, height = frame_size(frame)
    data = np.frombuffer(frame.get_buffer_as_uint16(), dtype=np.uint16)
    return data.reshape((height, width)).copy()


def try_enable_openni_registration(device, openni2):
    try:
        if hasattr(device, "set_depth_color_sync_enabled"):
            device.set_depth_color_sync_enabled(True)
    except Exception:
        pass

    registration_mode = getattr(openni2, "IMAGE_REGISTRATION_DEPTH_TO_COLOR", None)

    try:
        if hasattr(device, "set_image_registration_mode"):
            if registration_mode is not None:
                device.set_image_registration_mode(registration_mode)
            else:
                device.set_image_registration_mode(True)
    except Exception:
        pass


def create_openni_depth_stream(device, c_api):
    depth_stream = device.create_depth_stream()
    pixel_format = getattr(c_api.OniPixelFormat, "ONI_PIXEL_FORMAT_DEPTH_1_MM", None)
    if pixel_format is None:
        pixel_format = getattr(c_api.OniPixelFormat, "ONI_PIXEL_FORMAT_DEPTH_100_UM")

    depth_stream.set_video_mode(
        c_api.OniVideoMode(
            pixelFormat=pixel_format,
            resolutionX=640,
            resolutionY=480,
            fps=30,
        )
    )
    depth_stream.start()
    return depth_stream


def create_openni_color_stream(device, c_api):
    color_stream = device.create_color_stream()
    if color_stream is None:
        return None

    pixel_format = getattr(c_api.OniPixelFormat, "ONI_PIXEL_FORMAT_RGB888")
    color_stream.set_video_mode(
        c_api.OniVideoMode(
            pixelFormat=pixel_format,
            resolutionX=640,
            resolutionY=480,
            fps=30,
        )
    )
    color_stream.start()
    return color_stream


def capture_with_openni():
    openni2, c_api, package_name = import_openni_modules()

    device = None
    depth_stream = None
    color_stream = None
    redist_dir = detect_openni_redist()
    color_backend = None
    use_uvc_color = False
    last_color_rgb = None
    last_depth_mm = None

    try:
        log_step(
            f"Preparando captura de profundidad con OpenNI ({package_name}). Redist: {redist_dir if redist_dir is not None else 'auto'}"
        )
        if redist_dir is not None:
            if hasattr(os, "add_dll_directory"):
                os.add_dll_directory(str(redist_dir))
            openni2.initialize(str(redist_dir))
        else:
            openni2.initialize()

        log_step("OpenNI inicializado.")
        device = openni2.Device.open_any()
        log_step("Sensor Astra abierto.")
        try_enable_openni_registration(device, openni2)

        depth_stream = create_openni_depth_stream(device, c_api)
        log_step("Captura de profundidad iniciada.")

        try:
            color_stream = create_openni_color_stream(device, c_api)
        except Exception:
            color_stream = None

        if color_stream is None:
            log_step(
                "Profundidad obtenida con OpenNI. La imagen RGB se capturara con la "
                "camara color/UVC de la Astra. Este modo es aproximado, no sincronizado."
            )
            use_uvc_color = True
            color_backend = "UVC secuencial"
        else:
            log_step("OpenNI tambien entrego color RGB.")
            color_backend = "OpenNI"

        for _ in range(ASTRA_WARMUP_FRAMES):
            depth_frame = depth_stream.read_frame()
            last_depth_mm = openni_depth_stream_to_mm(depth_frame)
            if color_stream is not None:
                color_frame = color_stream.read_frame()
                last_color_rgb = openni_color_stream_to_rgb(color_frame)

        if last_depth_mm is None:
            raise RuntimeError("No pude capturar profundidad valida desde OpenNI.")
        if color_stream is not None and last_color_rgb is None:
            raise RuntimeError("No pude capturar color valido desde OpenNI.")
    except Exception as exc:
        raise RuntimeError(
            "No pude capturar desde OpenNI con la Astra.\n"
            f"Carpeta Redist detectada: {redist_dir if redist_dir is not None else 'ninguna'}\n"
            f"Error original: {exc}"
        ) from exc
    finally:
        if color_stream is not None:
            try:
                color_stream.stop()
            except Exception:
                pass
            try:
                color_stream.close()
            except Exception:
                pass
            color_stream = None
        if depth_stream is not None:
            try:
                depth_stream.stop()
            except Exception:
                pass
            try:
                depth_stream.close()
            except Exception:
                pass
            depth_stream = None
        if device is not None:
            try:
                device.close()
            except Exception:
                pass
            device = None

        # Evitamos openni2.unload() porque en este entorno estaba provocando
        # un crash nativo intermitente (0xc0000374) al salir del proceso.
        gc.collect()

    if use_uvc_color:
        log_step("Profundidad lista. Ahora capturare la imagen RGB desde la camara UVC de la Astra.")
        time.sleep(0.6)
        last_color_rgb, uvc_backend = capture_color_with_uvc()
        color_backend = uvc_backend

    if last_color_rgb is None or last_depth_mm is None:
        raise RuntimeError("No pude completar la captura de color y profundidad.")

    log_step("Captura de color y profundidad completada.")
    return CaptureResult(
        color_rgb=last_color_rgb,
        depth_mm=last_depth_mm,
        backend=f"OpenNI ({package_name}) + {color_backend}",
    )


def enum_name(value):
    return str(value).upper()


def frame_to_rgb_orbbec(color_frame):
    format_name = enum_name(color_frame.get_format())
    width = color_frame.get_width()
    height = color_frame.get_height()
    data = np.frombuffer(color_frame.get_data(), dtype=np.uint8)

    if "RGB" in format_name and "BGR" not in format_name:
        return data.reshape((height, width, 3)).copy()
    if "BGR" in format_name:
        return data.reshape((height, width, 3))[:, :, ::-1].copy()

    cv2 = import_cv2()

    if "MJPG" in format_name or "JPEG" in format_name:
        decoded = cv2.imdecode(data, cv2.IMREAD_COLOR)
        if decoded is None:
            raise RuntimeError("No pude decodificar el frame MJPG del Astra.")
        return cv2.cvtColor(decoded, cv2.COLOR_BGR2RGB)

    if "YUYV" in format_name or "YUY2" in format_name:
        yuyv = data.reshape((height, width, 2))
        return cv2.cvtColor(yuyv, cv2.COLOR_YUV2RGB_YUY2)

    if "UYVY" in format_name:
        uyvy = data.reshape((height, width, 2))
        return cv2.cvtColor(uyvy, cv2.COLOR_YUV2RGB_UYVY)

    raise RuntimeError(f"Formato de color no soportado por pyorbbecsdk: {format_name}")


def depth_frame_to_mm_orbbec(depth_frame):
    width = depth_frame.get_width()
    height = depth_frame.get_height()
    scale = depth_frame.get_depth_scale()
    depth_data = np.frombuffer(depth_frame.get_data(), dtype=np.uint16)
    depth_data = depth_data.reshape((height, width)).astype(np.float32)
    return (depth_data * scale).astype(np.uint16)


def capture_with_pyorbbecsdk():
    try:
        from pyorbbecsdk import (
            AlignFilter,
            Config,
            OBAlignMode,
            OBFrameAggregateOutputMode,
            OBSensorType,
            OBStreamType,
            Pipeline,
        )
    except ImportError as exc:
        raise RuntimeError(
            "No esta instalado 'pyorbbecsdk'. Este backend es el mejor candidato si tu Astra no entrega color por OpenNI."
        ) from exc

    pipeline = Pipeline()
    config = Config()
    align_filter = None
    selected_backend = "pyorbbecsdk sincronizado"

    try:
        color_profiles = pipeline.get_stream_profile_list(OBSensorType.COLOR_SENSOR)
        if color_profiles is None or len(color_profiles) == 0:
            raise RuntimeError("El Astra no reporto sensor de color en pyorbbecsdk.")

        selected_color_profile = None
        selected_depth_profile = None

        for i in range(len(color_profiles)):
            color_profile = color_profiles[i]
            try:
                depth_profiles = pipeline.get_d2c_depth_profile_list(
                    color_profile,
                    OBAlignMode.HW_MODE,
                )
            except Exception:
                continue

            if depth_profiles is not None and len(depth_profiles) > 0:
                selected_color_profile = color_profile
                selected_depth_profile = depth_profiles[0]
                config.enable_stream(selected_color_profile)
                config.enable_stream(selected_depth_profile)
                config.set_align_mode(OBAlignMode.HW_MODE)
                selected_backend = "pyorbbecsdk sincronizado (hardware D2C)"
                break

        if selected_color_profile is None:
            selected_color_profile = color_profiles.get_default_video_stream_profile()
            config.enable_stream(selected_color_profile)

            depth_profiles = pipeline.get_stream_profile_list(OBSensorType.DEPTH_SENSOR)
            if depth_profiles is None or len(depth_profiles) == 0:
                raise RuntimeError("El Astra no reporto sensor de profundidad.")

            selected_depth_profile = depth_profiles.get_default_video_stream_profile()
            config.enable_stream(selected_depth_profile)
            align_filter = AlignFilter(align_to_stream=OBStreamType.COLOR_STREAM)
            selected_backend = "pyorbbecsdk sincronizado (software D2C)"

        if hasattr(config, "set_frame_aggregate_output_mode"):
            config.set_frame_aggregate_output_mode(
                OBFrameAggregateOutputMode.FULL_FRAME_REQUIRE
            )

        pipeline.start(config)

        if hasattr(pipeline, "enable_frame_sync"):
            try:
                pipeline.enable_frame_sync()
            except Exception:
                pass

        last_color = None
        last_depth = None

        for _ in range(ASTRA_WARMUP_FRAMES):
            frames = pipeline.wait_for_frames(ASTRA_CAPTURE_TIMEOUT_MS)
            if frames is None:
                continue

            if align_filter is not None:
                frames = align_filter.process(frames)
                if not frames:
                    continue
                frames = frames.as_frame_set()

            color_frame = frames.get_color_frame()
            depth_frame = frames.get_depth_frame()
            if color_frame is None or depth_frame is None:
                continue

            last_color = frame_to_rgb_orbbec(color_frame)
            last_depth = depth_frame_to_mm_orbbec(depth_frame)

        if last_color is None or last_depth is None:
            raise RuntimeError("No pude capturar color y profundidad sincronizados con pyorbbecsdk.")

        return CaptureResult(
            color_rgb=last_color,
            depth_mm=last_depth,
            backend=selected_backend,
        )
    finally:
        try:
            pipeline.stop()
        except Exception:
            pass


def capture_from_astra():
    errors = []

    backends = {
        "openni": ("OpenNI", capture_with_openni),
        "pyorbbecsdk": ("pyorbbecsdk", capture_with_pyorbbecsdk),
    }

    if ASTRA_BACKEND in backends:
        ordered_backends = [backends[ASTRA_BACKEND]]
    else:
        ordered_backends = [
            ("pyorbbecsdk", capture_with_pyorbbecsdk),
            ("OpenNI", capture_with_openni),
        ]

    for name, fn in ordered_backends:
        try:
            return fn()
        except Exception as exc:
            errors.append(f"{name}: {exc}")

    joined = "\n".join(f"- {error}" for error in errors)
    raise RuntimeError(
        "No pude obtener una captura usable desde la Astra en la v1.\n"
        "La v1 usa OpenNI para profundidad y, si el SDK no entrega RGB, usa la camara color/UVC por separado.\n"
        "Si tu equipo no entrega color por OpenNI, intenta con ASTRA_BACKEND=pyorbbecsdk.\n"
        f"Detalles:\n{joined}"
    )


def main():
    log_step("Inicializando captura Astra v1...")
    capture = capture_from_astra()
    log_step(f"Backend detectado: {capture.backend}")

    ensure_output_dirs()
    timestamp = now_tag()
    base_name = f"pictures_v1_{timestamp}"
    raw_path = RAW_PHOTO_DIR / f"{base_name}.jpg"
    annotated_path = DETECTED_PHOTO_DIR / f"{base_name}_detectado.jpg"
    json_path = OUTPUT_DIR / f"{base_name}_vision.json"
    unity_json_path = UNITY_JSON_DIR / f"{base_name}_unity.json"

    save_raw_capture(capture.color_rgb, raw_path)
    log_step(f"Captura guardada: {raw_path}")

    log_step("Enviando imagen a Google Vision...")
    result = call_vision(encode_rgb_image(capture.color_rgb))
    save_json(result, json_path)
    log_step(f"Respuesta JSON guardada: {json_path}")
    log_step("Respuesta de Google Vision:")
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)

    responses = result.get("responses", [])
    if not responses:
        raise RuntimeError("La Vision API no devolvio respuestas.")

    response = responses[0]
    if "error" in response:
        raise RuntimeError(f"Vision API devolvio error: {response['error']}")

    detection_records = build_detection_records(capture.color_rgb, capture.depth_mm, response)
    annotate_capture(capture.color_rgb, detection_records, annotated_path)
    log_step(f"Imagen final guardada: {annotated_path}")

    unity_json = build_unity_json(
        capture,
        detection_records,
        raw_path,
        annotated_path,
        json_path,
    )
    save_json(unity_json, unity_json_path)
    log_step(f"JSON para Unity guardado: {unity_json_path}")


if __name__ == "__main__":
    main()
