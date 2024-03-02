import re

from matplotlib.pyplot import plot, show, xlim

address = []
size = []
type = []
pattern = r"\s+([a-z0-9]+)\s+([a-z0-9]+)\s+([a-z0-9\.]+)\s?(Free)?"
base = int("7f9b2c000020", 16)

with open("/home/vincent/Downloads/dump/heap.txt", "r") as file:
    file.readline()
    file.readline()

    while True:
        line = file.readline()

        if not line or line.startswith("Statistics"):
            break

        match = re.match(pattern, line)

        if match:
            address.append(int(match.group(1), 16) - base)
            size.append(int(match.group(3).replace(".", "")))
            type.append(1 if match.group(4) else 0)

plot(address, size, marker="o")

# Free space only
address_free = []
size_free = []

for i in range(1, len(type)):
    if (type[i] == 1):
        address_free.append(address[i])
        size_free.append(size[i])

plot(address_free, size_free, "ro")
show()