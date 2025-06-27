FROM node:alpine
MAINTAINER Mario Toffia

WORKDIR /src
ADD index.js /src
ADD package.txt /src/package.json

RUN npm install

EXPOSE  8080
CMD ["node", "index.js"]