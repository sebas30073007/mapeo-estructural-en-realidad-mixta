#!/usr/bin/env python3
"""
Extracción de BORDES INTERNOS del camino libre (blanco)
Las paredes que delimitan el pasillo por dentro
"""
import json
import math
from pathlib import Path
import cv2
import numpy as np
import yaml
from PIL import Image

# =========================================================
# CONFIGURACIÓN
# =========================================================
# Estos valores se sobrescriben si se pasan argumentos
MAP_YAML = "current_map.yaml"
OUTPUT_JSON = "walls_interior_borders.json"

# Leer argumentos de línea de comandos
import sys
if len(sys.argv) >= 3:
    MAP_YAML = sys.argv[1]
    OUTPUT_JSON = sys.argv[2]

# Umbral para ESPACIO LIBRE (blanco)
FREE_MIN = 252  # Píxeles >= 252 son considerados libres (blanco)

# Limpieza proporcional al tamaño del mapa
MIN_FREE_AREA_PERCENT = 0.01  # 1% del área total del mapa

# Morfología para espacio libre
MORPH_OPEN_SIZE = 3    # Eliminar ruido pequeño (era 10, demasiado agresivo)
MORPH_CLOSE_SIZE = 8   # Cerrar protuberancias pequeñas del contorno (era 5)

# Simplificación
APPROX_EPS_PX = 12.0   # Suaviza más el contorno antes de segmentar (era 5.0)

# Segmentos
MIN_SEG_LEN_PX = 5.0   # Acepta segmentos más cortos (era 13.5)

# NO ortogonalizar
FORCE_ORTHOGONAL = False

# Fusión de colineales
MERGE_COLINEAR = True
COLINEAR_ANGLE_TOL_DEG = 25.0   # Más tolerante para fusionar segmentos (era 20.0)
COLINEAR_DIST_TOL_PX = 15.0     # Mayor tolerancia de distancia perpendicular (era 12.0)
COLINEAR_GAP_TOL_PX = 12.0      # Gap más permisivo entre segmentos (era 15.0)

# Deduplicación
CORNER_TOL_PX = 3.0

# =========================================================
# FUNCIONES AUXILIARES
# =========================================================
def load_map_yaml(yaml_path):
    with open(yaml_path, "r") as f:
        return yaml.safe_load(f)

def load_pgm(img_path):
    return np.array(Image.open(img_path).convert("L"))

def pixel_to_world_m(px, py, origin, resolution, height):
    wx = origin[0] + px * resolution
    wy = origin[1] + (height - 1 - py) * resolution
    return wx, wy

# =========================================================
# EXTRACCIÓN DE ESPACIO LIBRE
# =========================================================
def extract_free_space(img, min_free):
    """Extrae píxeles blancos (espacio libre)"""
    free = np.zeros_like(img, dtype=np.uint8)
    free[img >= min_free] = 255
    return free

def get_largest_free_area(free_img, min_area):
    """Obtiene el área libre más grande (pasillo principal)"""
    num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(free_img, connectivity=8)
    
    if num_labels <= 1:
        return None
    
    largest_label = 1
    largest_area = stats[1, cv2.CC_STAT_AREA]
    
    for i in range(2, num_labels):
        area = stats[i, cv2.CC_STAT_AREA]
        if area > largest_area:
            largest_area = area
            largest_label = i
    
    if largest_area < min_area:
        return None
    
    main_free = np.zeros_like(free_img)
    main_free[labels == largest_label] = 255
    
    return main_free

def clean_free_space_morphology(free_img, open_size=3, close_size=5):
    """Aplica morfología al espacio libre"""
    kernel_open = cv2.getStructuringElement(cv2.MORPH_RECT, (open_size, open_size))
    kernel_close = cv2.getStructuringElement(cv2.MORPH_RECT, (close_size, close_size))
    
    cleaned = cv2.morphologyEx(free_img, cv2.MORPH_OPEN, kernel_open)
    cleaned = cv2.morphologyEx(cleaned, cv2.MORPH_CLOSE, kernel_close)
    
    return cleaned

# =========================================================
# PROCESAMIENTO DE CONTORNOS
# =========================================================
def get_interior_contour(free_area):
    """Obtiene el contorno INTERNO del espacio libre"""
    contours, hierarchy = cv2.findContours(free_area, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
    
    if not contours or hierarchy is None:
        return None
    
    return contours[0]

def contour_to_segments(approx, min_len):
    """Convierte contorno aproximado en segmentos"""
    segments = []
    pts = approx[:, 0, :]
    
    for i in range(len(pts)):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % len(pts)]
        
        length = np.hypot(x2 - x1, y2 - y1)
        
        if length >= min_len:
            segments.append((float(x1), float(y1), float(x2), float(y2)))
        elif length >= (min_len * 0.5):
            segments.append((float(x1), float(y1), float(x2), float(y2)))
    
    return segments

# =========================================================
# FUSIÓN DE SEGMENTOS COLINEALES
# =========================================================
def normalize_segment(seg):
    """Normaliza segmento para que p1 <= p2"""
    x1, y1, x2, y2 = seg
    if (x1, y1) <= (x2, y2):
        return (x1, y1, x2, y2)
    else:
        return (x2, y2, x1, y1)

def segment_angle(seg):
    """Ángulo del segmento en radianes [0, π]"""
    x1, y1, x2, y2 = seg
    angle = np.arctan2(y2 - y1, x2 - x1)
    if angle < 0:
        angle += np.pi
    return angle

def segment_length(seg):
    """Longitud del segmento"""
    x1, y1, x2, y2 = seg
    return np.hypot(x2 - x1, y2 - y1)

def point_to_line_distance(px, py, x1, y1, x2, y2):
    """Distancia de punto a línea infinita"""
    dx = x2 - x1
    dy = y2 - y1
    
    if abs(dx) < 0.001 and abs(dy) < 0.001:
        return np.hypot(px - x1, py - y1)
    
    num = abs(dy * px - dx * py + (dx * y1 - dy * x1))
    den = np.hypot(dx, dy)
    
    return num / den

def merge_colinear_segments(segments, angle_tol_deg=15.0, dist_tol_px=8.0, gap_tol_px=10.0):
    """Fusiona segmentos casi colineales en líneas largas continuas"""
    if not segments:
        return []
    
    segs = [normalize_segment(s) for s in segments]
    
    merged = []
    used = [False] * len(segs)
    
    for i in range(len(segs)):
        if used[i]:
            continue
        
        x1, y1, x2, y2 = segs[i]
        base_angle = segment_angle((x1, y1, x2, y2))
        
        group = [(x1, y1, x2, y2)]
        used[i] = True
        
        changed = True
        iterations = 0
        max_iterations = 10
        
        while changed and iterations < max_iterations:
            changed = False
            iterations += 1
            
            for j in range(len(segs)):
                if used[j]:
                    continue
                
                x3, y3, x4, y4 = segs[j]
                seg_angle_j = segment_angle((x3, y3, x4, y4))
                
                angle_diff = abs(base_angle - seg_angle_j)
                if angle_diff > np.pi:
                    angle_diff = 2 * np.pi - angle_diff
                
                if np.degrees(angle_diff) > angle_tol_deg:
                    continue
                
                is_near = False
                
                for gx1, gy1, gx2, gy2 in group:
                    d_g2_j1 = np.hypot(gx2 - x3, gy2 - y3)
                    d_g2_j2 = np.hypot(gx2 - x4, gy2 - y4)
                    d_g1_j1 = np.hypot(gx1 - x3, gy1 - y3)
                    d_g1_j2 = np.hypot(gx1 - x4, gy1 - y4)
                    
                    min_dist = min(d_g2_j1, d_g2_j2, d_g1_j1, d_g1_j2)
                    
                    if min_dist < gap_tol_px:
                        is_near = True
                        break
                
                if not is_near:
                    continue
                
                d1 = point_to_line_distance(x3, y3, x1, y1, x2, y2)
                d2 = point_to_line_distance(x4, y4, x1, y1, x2, y2)
                
                if max(d1, d2) > dist_tol_px:
                    continue
                
                group.append((x3, y3, x4, y4))
                used[j] = True
                changed = True
        
        if len(group) == 1:
            if segment_length(group[0]) >= MIN_SEG_LEN_PX:
                merged.append(group[0])
        else:
            all_points = []
            for seg in group:
                all_points.append((seg[0], seg[1]))
                all_points.append((seg[2], seg[3]))
            
            direction = np.array([np.cos(base_angle), np.sin(base_angle)])
            
            projections = [np.dot([p[0], p[1]], direction) for p in all_points]
            
            min_idx = np.argmin(projections)
            max_idx = np.argmax(projections)
            
            p_start = all_points[min_idx]
            p_end = all_points[max_idx]
            
            final_seg = (p_start[0], p_start[1], p_end[0], p_end[1])
            if segment_length(final_seg) >= MIN_SEG_LEN_PX:
                merged.append(final_seg)
    
    return merged

# =========================================================
# DEDUPLICACIÓN
# =========================================================
def deduplicate_points(points, tol):
    """Elimina puntos duplicados"""
    if not points:
        return []
    
    unique = [points[0]]
    for p in points[1:]:
        is_dup = False
        for u in unique:
            if abs(p[0] - u[0]) < tol and abs(p[1] - u[1]) < tol:
                is_dup = True
                break
        if not is_dup:
            unique.append(p)
    
    return unique

# =========================================================
# DETECCIÓN DE PERSONAS (RANSAC CIRCULAR)
# =========================================================
# Parámetros de detección de personas
PERSON_RADIUS_MIN_M = 0.05   # Radio mínimo: 5 cm (diámetro 10 cm)
PERSON_RADIUS_MAX_M = 0.30   # Radio máximo: 30 cm (persona realista en LIDAR 2D)
PERSON_ARC_MIN_DEG = 60.0    # El arco detectado debe cubrir al menos 60°
PERSON_ARC_MAX_DEG = 300.0   # No más de 300° (si es círculo completo, es objeto, no persona)
PERSON_RANSAC_INLIER_DIST_M = 0.02  # Tolerancia RANSAC: 2 cm sobre el radio
PERSON_RANSAC_ITERATIONS = 120       # Iteraciones RANSAC por ventana
PERSON_WINDOW_PX = 30               # Puntos del contorno por ventana deslizante
PERSON_WINDOW_STEP_PX = 8           # Paso entre ventanas
PERSON_MIN_INLIERS = 6              # Mínimo de inliers para aceptar candidato
PERSON_CENTER_MERGE_M = 0.15        # Fusiona centros a menos de 15 cm entre sí


def _fit_circle_3pts(p1, p2, p3):
    """
    Ajusta un círculo a 3 puntos. Devuelve (cx, cy, r) o None si son colineales.
    Puntos en metros (floats).
    """
    ax, ay = p1
    bx, by = p2
    cx, cy = p3

    D = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by))
    if abs(D) < 1e-10:
        return None  # Colineales

    ux = ((ax**2 + ay**2) * (by - cy) +
          (bx**2 + by**2) * (cy - ay) +
          (cx**2 + cy**2) * (ay - by)) / D

    uy = ((ax**2 + ay**2) * (cx - bx) +
          (bx**2 + by**2) * (ax - cx) +
          (cx**2 + cy**2) * (bx - ax)) / D

    r = np.hypot(ax - ux, ay - uy)
    return (ux, uy, r)


def _arc_span_deg(pts_m, cx, cy):
    """
    Calcula cuántos grados del círculo cubren los puntos inliers.
    Devuelve el ángulo total del arco convexo en grados.
    """
    angles = [np.degrees(np.arctan2(py - cy, px - cx)) for px, py in pts_m]
    angles_sorted = sorted(angles)

    # Máximo gap entre ángulos consecutivos (en círculo cerrado)
    gaps = []
    for i in range(len(angles_sorted) - 1):
        gaps.append(angles_sorted[i + 1] - angles_sorted[i])
    # Gap que cierra el círculo
    gaps.append(360.0 - angles_sorted[-1] + angles_sorted[0])

    max_gap = max(gaps)
    arc_span = 360.0 - max_gap
    return arc_span


def detecta_personas(contour_raw_px, origin, resolution, height):
    """
    Detecta personas como medias lunas (arcos circulares) en el contorno
    crudo del espacio libre usando RANSAC circular con ventana deslizante.

    Parámetros
    ----------
    contour_raw_px : np.ndarray  shape (N,1,2)  contorno de cv2.findContours
    origin         : list [ox, oy, ...]          origen del mapa en metros
    resolution     : float                        metros/píxel
    height         : int                          altura de la imagen en píxeles

    Devuelve
    --------
    list de dicts: [{"x": cx_m, "y": cy_m, "radio_m": r, "arco_deg": span}, ...]
    """
    if contour_raw_px is None or len(contour_raw_px) < PERSON_MIN_INLIERS:
        return []

    # Convertir contorno a metros
    pts_px = contour_raw_px[:, 0, :]   # shape (N, 2)
    pts_m = np.array([
        pixel_to_world_m(float(p[0]), float(p[1]), origin, resolution, height)
        for p in pts_px
    ])  # shape (N, 2)

    N = len(pts_m)
    candidatos = []  # Lista de (cx, cy, r, arc_span, n_inliers)

    rng = np.random.default_rng(42)

    # Ventana deslizante sobre los puntos del contorno (circular)
    for start in range(0, N, PERSON_WINDOW_STEP_PX):
        # Extraer ventana con wrap-around circular
        indices = [(start + k) % N for k in range(PERSON_WINDOW_PX)]
        window = pts_m[indices]  # (PERSON_WINDOW_PX, 2)

        if len(window) < 3:
            continue

        best_inliers = []
        best_circle = None

        for _ in range(PERSON_RANSAC_ITERATIONS):
            # Muestrear 3 puntos al azar de la ventana
            idx3 = rng.choice(len(window), size=3, replace=False)
            p1, p2, p3 = window[idx3[0]], window[idx3[1]], window[idx3[2]]

            circle = _fit_circle_3pts(p1, p2, p3)
            if circle is None:
                continue

            cx, cy, r = circle

            # Filtrar por rango de radio (tamaño de persona)
            if not (PERSON_RADIUS_MIN_M <= r <= PERSON_RADIUS_MAX_M):
                continue

            # Calcular inliers: puntos cuya distancia al círculo <= tolerancia
            dists = np.abs(np.hypot(window[:, 0] - cx, window[:, 1] - cy) - r)
            inlier_mask = dists <= PERSON_RANSAC_INLIER_DIST_M
            n_inliers = int(np.sum(inlier_mask))

            if n_inliers > len(best_inliers):
                best_inliers = list(np.where(inlier_mask)[0])
                best_circle = (cx, cy, r)

        if best_circle is None or len(best_inliers) < PERSON_MIN_INLIERS:
            continue

        cx, cy, r = best_circle
        inlier_pts = [tuple(window[i]) for i in best_inliers]

        # Verificar que el arco cubra un rango angular suficiente (media luna)
        arc_span = _arc_span_deg(inlier_pts, cx, cy)

        if not (PERSON_ARC_MIN_DEG <= arc_span <= PERSON_ARC_MAX_DEG):
            continue

        candidatos.append((cx, cy, r, arc_span, len(best_inliers)))

    if not candidatos:
        return []

    # Fusionar candidatos cercanos (mismo objeto detectado por varias ventanas)
    fusionados = []
    usados = [False] * len(candidatos)

    for i, (cx_i, cy_i, r_i, arc_i, nin_i) in enumerate(candidatos):
        if usados[i]:
            continue
        grupo = [(cx_i, cy_i, r_i, arc_i, nin_i)]
        usados[i] = True

        for j, (cx_j, cy_j, r_j, arc_j, nin_j) in enumerate(candidatos):
            if usados[j]:
                continue
            dist = np.hypot(cx_i - cx_j, cy_i - cy_j)
            if dist < PERSON_CENTER_MERGE_M:
                grupo.append((cx_j, cy_j, r_j, arc_j, nin_j))
                usados[j] = True

        # Elegir el del grupo con más inliers como representativo
        mejor = max(grupo, key=lambda g: g[4])
        fusionados.append({
            "x": round(mejor[0], 4),
            "y": round(mejor[1], 4),
            "radio_m": round(mejor[2], 4),
            "arco_deg": round(mejor[3], 1)
        })

    return fusionados


def main():
    print("=" * 60)
    print("EXTRACCIÓN DE BORDES INTERNOS DEL CAMINO LIBRE")
    print("=" * 60)
    
    # Cargar mapa
    meta = load_map_yaml(MAP_YAML)
    img_path = Path(MAP_YAML).parent / meta["image"]
    
    resolution = float(meta["resolution"])
    origin = meta["origin"]
    
    img = load_pgm(img_path)
    h, w = img.shape[:2]
    
    print(f"📐 Mapa: {w}×{h} px, resolución: {resolution}m/px")
    print(f"📍 Origen: {origin}")
    
    # Calcular umbral de área mínima proporcional
    total_area = h * w
    min_free_area = int(total_area * MIN_FREE_AREA_PERCENT)
    print(f"📊 Área mínima calculada: {min_free_area} px ({MIN_FREE_AREA_PERCENT*100}% del mapa)")
    
    # 1) Extraer espacio libre (blanco)
    free_space = extract_free_space(img, FREE_MIN)
    print(f"✅ Píxeles libres iniciales: {np.count_nonzero(free_space)}")
    
    # 2) Limpieza morfológica
    free_space = clean_free_space_morphology(free_space, MORPH_OPEN_SIZE, MORPH_CLOSE_SIZE)
    print(f"✅ Después de morfología: {np.count_nonzero(free_space)} píxeles")
    
    # 3) Obtener área libre principal
    main_free = get_largest_free_area(free_space, min_free_area)
    
    if main_free is None:
        print("❌ No se encontró área libre suficientemente grande")
        # Devolver JSON vacío
        output_data = {
            "version": 3,
            "unit": "m",
            "extraction_type": "interior_borders",
            "map_info": {
                "image": meta["image"],
                "resolution": resolution,
                "origin": origin,
                "width": w,
                "height": h
            },
            "wall_segments": [],
            "corners": [],
            "personas": []
        }
        with open(OUTPUT_JSON, "w") as f:
            json.dump(output_data, f, indent=2)
        print(f"✅ Guardado JSON vacío: {OUTPUT_JSON}")
        return
    
    print(f"✅ Área libre principal detectada")
    
    # 4) Obtener contorno INTERNO
    interior_contour = get_interior_contour(main_free)
    
    if interior_contour is None:
        print("❌ No se pudo extraer contorno interior")
        return
    
    print(f"✅ Contorno interior: {len(interior_contour)} puntos")
    
    # 5) Aproximar contorno
    approx = cv2.approxPolyDP(interior_contour, APPROX_EPS_PX, closed=True)
    print(f"✅ Después de simplificación: {len(approx)} vértices")
    
    # 6) Convertir a segmentos
    segments_px = contour_to_segments(approx, MIN_SEG_LEN_PX)
    print(f"✅ Segmentos iniciales: {len(segments_px)}")
    
    # 7) Fusionar segmentos colineales
    if MERGE_COLINEAR:
        prev_count = len(segments_px)
        max_iterations = 5  # Aumentado de 3 a 5 para más pasadas de fusión
        
        for iteration in range(max_iterations):
            segments_px = merge_colinear_segments(
                segments_px, 
                COLINEAR_ANGLE_TOL_DEG, 
                COLINEAR_DIST_TOL_PX,
                COLINEAR_GAP_TOL_PX
            )
            
            new_count = len(segments_px)
            print(f"  Iteración {iteration+1}: {new_count} segmentos")
            
            if new_count == prev_count:
                break
            
            prev_count = new_count
        
        print(f"✅ Después de fusionar colineales: {len(segments_px)}")
    
    # 8) Convertir a coordenadas del mundo
    segments_world = []
    all_corners_px = []
    
    for x1, y1, x2, y2 in segments_px:
        wx1, wy1 = pixel_to_world_m(x1, y1, origin, resolution, h)
        wx2, wy2 = pixel_to_world_m(x2, y2, origin, resolution, h)
        
        segments_world.append({
            "x1": wx1, "y1": wy1,
            "x2": wx2, "y2": wy2
        })
        
        all_corners_px.append((x1, y1))
        all_corners_px.append((x2, y2))
    
    # 9) Deduplicar esquinas
    all_corners_px = deduplicate_points(all_corners_px, CORNER_TOL_PX)
    
    corners_world = []
    for px, py in all_corners_px:
        wx, wy = pixel_to_world_m(px, py, origin, resolution, h)
        corners_world.append({"x": wx, "y": wy})

    # 10) Detectar personas con RANSAC circular sobre el contorno crudo
    print("\n🔍 Detectando personas (RANSAC circular)...")
    personas = detecta_personas(interior_contour, origin, resolution, h)
    print(f"✅ Personas detectadas: {len(personas)}")
    for i, p in enumerate(personas):
        print(f"   Persona {i+1}: x={p['x']}m  y={p['y']}m  "
              f"radio={p['radio_m']}m  arco={p['arco_deg']}°")
    
    # 11) Guardar JSON
    output_data = {
        "version": 3,
        "unit": "m",
        "extraction_type": "interior_borders",
        "map_info": {
            "image": meta["image"],
            "resolution": resolution,
            "origin": origin,
            "width": w,
            "height": h
        },
        "wall_segments": segments_world,
        "corners": corners_world,
        "personas": personas
    }
    
    with open(OUTPUT_JSON, "w") as f:
        json.dump(output_data, f, indent=2)
    
    print(f"\n✅ Guardado: {OUTPUT_JSON}")
    print(f"📊 {len(segments_world)} segmentos de pared")
    print(f"📍 {len(corners_world)} esquinas")
    print(f"🧍 {len(personas)} personas")
    print("=" * 60)

if __name__ == "__main__":
    main()