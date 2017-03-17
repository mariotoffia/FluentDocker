FROM mhart/alpine-node
MAINTAINER Mario Toffia <mario.toffia@dataductus.se>

WORKDIR /src
ADD index.js /src
ADD package.txt /src/package.json

RUN npm install

EXPOSE  8080
CMD ["node", "index.js"]