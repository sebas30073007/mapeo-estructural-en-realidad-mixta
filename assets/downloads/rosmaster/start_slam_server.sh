# Cargar entorno de ROS2 Jazzy
source /opt/ros/jazzy/setup.bash

# Cargar tu workspace
source /home/iberomsc02/ros2_ws_02/install/setup.bash

# Ejecutar servidor Flask
exec python3 /home/iberomsc02/slam_server.py
