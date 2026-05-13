import json
import numpy as np
from datetime import datetime

def calcular_probabilidad_persona(pos_actual, pos_previa, tiempo_actual, tiempo_previo, prob_anterior):
    """
    Calcula la probabilidad de que un objeto sea una persona basándose en:
    1. Movimiento relativo (desplazamiento).
    2. Tiempo transcurrido (Time Decay).
    """
    # 1. Calcular Delta Tiempo (en minutos)
    fmt = "%Y-%m-%d %H:%M:%S"
    t1 = datetime.strptime(tiempo_previo, fmt)
    t2 = datetime.strptime(tiempo_actual, fmt)
    delta_t = (t2 - t1).total_seconds() / 60.0 # Minutos
    
    # 2. Calcular Desplazamiento (Distancia Euclidiana)
    distancia = np.hypot(pos_actual['x'] - pos_previa['x'], 
                         pos_actual['y'] - pos_previa['y'])
    
    nueva_prob = prob_anterior
    
    # LÓGICA DE PROBABILIDADES
    # Si se movió más de 15cm en poco tiempo (es dinámico)
    if distancia > 0.15 and delta_t <= 5:
        nueva_prob += 0.3  # Aumenta mucho la probabilidad
        
    # Si pasaron más de 120 min (2 hrs) y no se ha movido (distancia < 5cm)
    elif delta_t >= 120 and distancia < 0.05:
        nueva_prob -= 0.4  # Es muy probable que sea un mueble u objeto estático
        
    # Si se movió pero pasó mucho tiempo (podría ser ruido o un objeto movido)
    elif delta_t > 30 and distancia > 0.10:
        nueva_prob += 0.1
        
    # Ajustar límites (clipping) entre 0 y 1
    return max(0.0, min(1.0, nueva_prob))

def actualizar_memoria_deteccion(nuevo_objeto, path_json="robot_state.json"):
    """
    Carga el estado anterior, compara y actualiza.
    """
    try:
        with open(path_json, 'r') as f:
            data = json.load(f)
    except FileNotFoundError:
        # Si no existe, inicializamos uno
        data = {
            "last_slam_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            "detected_objects": []
        }

    tiempo_ahora = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    if data["detected_objects"]:
        obj_previo = data["detected_objects"][0] # Asumimos seguimiento de un objeto
        
        prob_final = calcular_probabilidad_persona(
            nuevo_objeto["pos"], 
            obj_previo["pos"], 
            tiempo_ahora, 
            obj_previo["timestamp"], 
            obj_previo["prob"]
        )
    else:
        prob_final = 0.5 # Probabilidad inicial neutra
    
    # Actualizar para la siguiente corrida
    data["detected_objects"] = [{
        "pos": nuevo_objeto["pos"],
        "prob": prob_final,
        "timestamp": tiempo_ahora
    }]
    data["last_slam_time"] = tiempo_ahora
    
    with open(path_json, 'w') as f:
        json.dump(data, f, indent=2)
        
    return prob_final