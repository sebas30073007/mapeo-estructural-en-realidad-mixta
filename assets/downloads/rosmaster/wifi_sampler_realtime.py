#!/usr/bin/env python3
"""
Muestreador WiFi en tiempo real para ROS2
Obtiene la pose del robot en frame 'map' usando TF
y envía muestras por UDP a Unity.
"""

import rclpy
from rclpy.node import Node
from nav_msgs.msg import Odometry
from tf2_ros import Buffer, TransformListener, LookupException, ConnectivityException, ExtrapolationException
import socket
import json
import math
import subprocess
import re
import yaml
import os


class WiFiSamplerNode(Node):
    def __init__(self):
        super().__init__('wifi_sampler')

        # Parámetros configurables
        self.declare_parameter('unity_ip', '100.119.230.103')
        self.declare_parameter('unity_port', 5007)
        self.declare_parameter('sample_distance', 1.0)
        self.declare_parameter('ssid', '')

        self.unity_ip = self.get_parameter('unity_ip').value
        self.unity_port = self.get_parameter('unity_port').value
        self.sample_distance = self.get_parameter('sample_distance').value
        self.ssid = self.get_parameter('ssid').value

        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

        self.last_sample_pos = None
        self.sample_count = 0

        self.map_yaml_path = '/home/iberomsc02/maps/current_map.yaml'

        # TF
        self.tf_buffer = Buffer()
        self.tf_listener = TransformListener(self.tf_buffer, self)

        # Usamos /odom solo como trigger periódico
        self.odom_sub = self.create_subscription(
            Odometry,
            '/odom',
            self.odom_callback,
            10
        )

        self.get_logger().info('📡 WiFi Sampler iniciado (frame objetivo: map)')
        self.get_logger().info(f'   Unity: {self.unity_ip}:{self.unity_port}')
        self.get_logger().info(f'   Distancia entre muestras: {self.sample_distance}m')

    # ------------------------------------------------------------
    # Callback de odometría: solo dispara chequeo / toma de muestra
    # ------------------------------------------------------------
    def odom_callback(self, msg):
        pose = self.get_robot_pose_in_map()
        if pose is None:
            return

        x, y = pose

        if self.should_take_sample(x, y):
            self.take_wifi_sample(x, y)

    # ------------------------------------------------------------
    # Obtener pose del robot en frame map
    # ------------------------------------------------------------
    def get_robot_pose_in_map(self):
        candidate_frames = ['base_link', 'base_footprint']

        for source_frame in candidate_frames:
            try:
                transform = self.tf_buffer.lookup_transform(
                    'map',
                    source_frame,
                    rclpy.time.Time()
                )

                x = transform.transform.translation.x
                y = transform.transform.translation.y
                return x, y

            except (LookupException, ConnectivityException, ExtrapolationException):
                continue

        self.get_logger().warning(
            'No se pudo obtener TF map->base_link ni map->base_footprint',
            throttle_duration_sec=5
        )
        return None

    # ------------------------------------------------------------
    # Decidir si ya se recorrió suficiente distancia
    # ------------------------------------------------------------
    def should_take_sample(self, x, y):
        if self.last_sample_pos is None:
            return True

        dx = x - self.last_sample_pos[0]
        dy = y - self.last_sample_pos[1]
        return math.sqrt(dx * dx + dy * dy) >= self.sample_distance

    # ------------------------------------------------------------
    # Leer origen del mapa desde current_map.yaml
    # ------------------------------------------------------------
    def get_map_origin(self):
        try:
            if not os.path.exists(self.map_yaml_path):
                self.get_logger().warning(f'No se encontró {self.map_yaml_path}, usando origen (0,0)')
                return 0.0, 0.0

            with open(self.map_yaml_path, 'r') as f:
                data = yaml.safe_load(f)
                origin = data.get('origin', [0.0, 0.0, 0.0])
                return float(origin[0]), float(origin[1])

        except Exception as e:
            self.get_logger().warning(f'Error leyendo YAML del mapa: {e}')
            return 0.0, 0.0

    # ------------------------------------------------------------
    # Tomar una muestra y enviarla a Unity
    # ------------------------------------------------------------
    def take_wifi_sample(self, x, y):
        rssi = self.measure_wifi_rssi()
        bssid = self.get_bssid()
        map_origin_x, map_origin_y = self.get_map_origin()

        sample = {
            "type": "sample",
            "t": self.get_clock().now().nanoseconds / 1e9,
            "ssid": self.ssid if self.ssid else self.get_connected_ssid(),
            "bssid": bssid,
            "rssi": rssi,
            "row": 0,
            "col": 0,
            "x": float(x),               # coordenada global en frame map
            "y": float(y),               # coordenada global en frame map
            "k": self.sample_count,
            "map_origin_x": map_origin_x,
            "map_origin_y": map_origin_y
        }

        try:
            self.sock.sendto(
                json.dumps(sample).encode('utf-8'),
                (self.unity_ip, self.unity_port)
            )

            self.get_logger().info(
                f'📍 Muestra {self.sample_count}: '
                f'pos_map=({x:.2f}, {y:.2f}), '
                f'RSSI={rssi}, '
                f'origen=({map_origin_x:.3f}, {map_origin_y:.3f})'
            )

            self.last_sample_pos = (x, y)
            self.sample_count += 1

        except Exception as e:
            self.get_logger().error(f'Error enviando muestra UDP: {e}')

    # ------------------------------------------------------------
    # Medir RSSI del AP asociado
    # ------------------------------------------------------------
    def measure_wifi_rssi(self):
        try:
            result = subprocess.run(
                ['iw', 'wlan0', 'link'],
                capture_output=True,
                text=True,
                timeout=2
            )
            match = re.search(r'signal: (-\d+) dBm', result.stdout)
            if match:
                return int(match.group(1))
        except Exception as e:
            self.get_logger().warning(f'iw falló: {e}')

        return -70

    # ------------------------------------------------------------
    # Obtener BSSID
    # ------------------------------------------------------------
    def get_bssid(self):
        try:
            result = subprocess.run(
                ['iw', 'wlan0', 'link'],
                capture_output=True,
                text=True,
                timeout=2
            )
            match = re.search(r'Connected to ([0-9a-f:]{17})', result.stdout)
            if match:
                return match.group(1)
        except Exception:
            pass

        return "00:00:00:00:00:00"

    # ------------------------------------------------------------
    # Obtener SSID actual
    # ------------------------------------------------------------
    def get_connected_ssid(self):
        try:
            result = subprocess.run(
                ['iw', 'wlan0', 'link'],
                capture_output=True,
                text=True,
                timeout=2
            )
            match = re.search(r'SSID: (.+)', result.stdout)
            if match:
                return match.group(1).strip()
        except Exception:
            pass

        return "unknown"

    # ------------------------------------------------------------
    # Cleanup
    # ------------------------------------------------------------
    def destroy_node(self):
        try:
            self.sock.close()
        except Exception:
            pass

        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = WiFiSamplerNode()

    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('⏹️  Detenido por usuario')
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
