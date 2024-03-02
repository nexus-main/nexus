import os
from pathlib import Path

import setuptools

source_dir = os.getcwd()

build_dir = "../../../artifacts/obj/python-client"
Path(build_dir).mkdir(parents=True, exist_ok=True)
os.chdir(build_dir)

with open(os.path.join(source_dir, "README.md"), "r") as fh:
    long_description = fh.read()

# setuptools normalizes SemVer version :-/ https://github.com/pypa/setuptools/issues/308
# The solution suggested there (from setuptools import sic, then call sic(version))
# is useless here because setuptools calls packaging.version.Version when .egg is created
# which again normalizes the version.

setuptools.setup(
    name="nexus-api",
    version=str(os.getenv("PYPI_VERSION")),
    description="Client for the Nexus system.",
    long_description=long_description,
    long_description_content_type="text/markdown",
    author=str(os.getenv("AUTHORS")),
    url="https://github.com/malstroem-labs/nexus",
    packages=[
        "nexus_api"
    ],
    project_urls={
        "Project": os.getenv("PACKAGEPROJECTURL"),
        "Repository": os.getenv("REPOSITORYURL"),
    },
    classifiers=[
        "Programming Language :: Python :: 3",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent"
    ],
    license=str(os.getenv("PACKAGELICENSEEXPRESSION")),
    keywords="Nexus time-series data lake",
    platforms=[
        "any"
    ],
    package_dir={
        "nexus_api": os.path.join(source_dir, "nexus_api")
    },
    python_requires=">=3.9",
    install_requires=[
        "httpx>=0.22.0"
    ]
)
