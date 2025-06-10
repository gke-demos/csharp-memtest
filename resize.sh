#!/bin/sh

kubectl patch pod csharp-memtest --subresource resize --patch \
  '{"spec":{"containers":[{"name":"csharp-memtest", "resources":{"requests":{"memory":"2G"},"limits":{"memory":"2G"}}}]}}'