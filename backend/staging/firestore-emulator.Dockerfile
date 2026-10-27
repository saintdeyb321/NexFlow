# The disposable emulator deliberately matches the Node and Java major versions
# used by the isolated CI workflow. It has no Firebase credentials or project
# configuration embedded in the image.
FROM eclipse-temurin:21-jre AS temurin

FROM node:22.14.0-bookworm-slim
COPY --from=temurin /opt/java/openjdk /opt/java/openjdk
ENV JAVA_HOME=/opt/java/openjdk
ENV PATH="${JAVA_HOME}/bin:${PATH}"

RUN java -version \
    && npm install --global firebase-tools@14.12.0 \
    && firebase setup:emulators:firestore

WORKDIR /emulator
