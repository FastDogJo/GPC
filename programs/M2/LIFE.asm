; LIFE.asm - Conway's Life on the Model II 640x240 hires screen (1 pixel cells, wraps top/bottom/left/right)
; Assembled with zmac, loaded into an integer BASIC array and run through USR (see LIFE.bas)
; Uses absolute addresses: LIFE.bas relocates them (assembled at org 0 and org 100h, the differences give the offsets)
; Buffers live in the array after the code.
;
; Word cells set from BASIC:  C(1) = mode (0 = random fill, else one generation)   C(2) = random seed (non-zero)

PX      equ 80h                 ; hires X (byte column 0-79)
PY      equ 81h                 ; hires Y (row 0-239)
PD      equ 82h                 ; hires data (8 pixels, bit 7 = leftmost)

; B = pixel to the left, C = self, D = pixel to the right (bytes at ix+dm, ix+d0, ix+dp)
neigh   macro dm,d0,dp
        ld a,(ix+dm)
        rrca                    ; carry = bit 0 of byte to the left
        ld a,(ix+d0)
        ld c,a
        rra                     ; (carry << 7) | (self >> 1)
        ld b,a
        ld a,(ix+dp)
        rlca                    ; carry = bit 7 of byte to the right
        ld a,c
        rla                     ; (self << 1) | carry
        ld d,a
        endm

; D = bit 0, E = bit 1 of (left + self + right) for each of the 8 pixels
tri     macro dm,d0,dp
        neigh dm,d0,dp
        ld a,b
        and c
        ld e,a
        ld a,b
        or c
        and d
        or e
        ld e,a
        ld a,b
        xor c
        xor d
        ld d,a
        endm

        org ORG
start:  jr entry
mode:   dw 0                    ; C(1) mode
seed:   dw 0                    ; C(2) seed
rowv:   dw 0                    ; current row
colv:   dw 0                    ; current byte column

entry:  push af
        push bc
        push de
        push hl
        push ix
        ld a,(mode)
        or a
        jr z,fill
        call step
        jr done
fill:   call randfill
done:   pop ix
        pop hl
        pop de
        pop bc
        pop af
        ret

; ---- random fill (about 25% alive) ----
randfill:
        ld d,0
rfrow:  ld e,0
rfcol:  call rand
        ld a,h
        and l
        ld c,a
        ld a,e
        out (PX),a
        ld a,d
        out (PY),a
        ld a,c
        out (PD),a
        inc e
        ld a,e
        cp 80
        jr nz,rfcol
        inc d
        ld a,d
        cp 240
        jr nz,rfrow
        ret

; 16 bit xorshift, seed in (seed); returns HL
rand:   ld hl,(seed)
        ld a,h
        rra
        ld a,l
        rra
        xor h
        ld h,a
        ld a,l
        rra
        ld a,h
        rra
        xor l
        ld l,a
        xor h
        ld h,a
        ld (seed),hl
        ret

; A = screen row, HL = 82 byte buffer: [0]=copy of byte 79, [1..80]=row, [81]=copy of byte 0
readrow:
        ld d,a
        inc hl
        push hl
        ld e,0
        ld b,80
rrlp:   ld a,e
        out (PX),a
        ld a,d
        out (PY),a
        in a,(PD)
        ld (hl),a
        inc hl
        inc e
        djnz rrlp
        pop hl
        ld a,(hl)
        ld de,80
        add hl,de
        ld (hl),a
        dec hl
        ld a,(hl)
        ld de,-80
        add hl,de
        ld (hl),a
        ret

; ---- one generation: rows are read from the screen, the new row is written over it ----
step:   ld hl,prev
        ld a,239
        call readrow
        ld hl,cur
        xor a
        call readrow
        ld hl,cur               ; keep original row 0, it is the "next" row of row 239
        ld de,save
        ld bc,82
        ldir
        xor a
        ld (rowv),a

rowlp:  ld a,(rowv)
        cp 239
        jr z,lastrow
        inc a
        ld hl,next
        call readrow
        jr havenx
lastrow:
        ld hl,save
        ld de,next
        ld bc,82
        ldir

havenx: xor a
        ld (colv),a
        ld ix,cur+1             ; IX = &cur[col 0]

bytelp: tri -83,-82,-81         ; row above  : D=a0 E=a1
        push de
        tri 81,82,83            ; row below  : D=b0 E=b1
        pop bc                  ; B=a0 C=a1
        ld a,b
        and d
        ld h,a                  ; H = a0&b0
        ld a,b
        xor d
        ld b,a                  ; B = s0
        ld a,c
        xor e
        ld l,a                  ; L = a1^b1
        ld a,c
        and e
        ld c,a                  ; C = a1&b1
        ld a,l
        and h
        or c
        ld c,a                  ; C = s2
        ld a,l
        xor h
        ld d,a                  ; D = s1
        push bc
        push de
        neigh -1,0,1            ; this row: B=left C=self D=right
        ld a,b
        and d
        ld e,a                  ; E = c1
        ld a,b
        xor d
        ld d,a                  ; D = c0
        pop hl                  ; H = s1
        pop bc                  ; B = s0  C = s2
        ld a,b
        and d
        ld l,a                  ; L = s0&c0
        ld a,b
        xor d
        ld b,a                  ; B = t0
        ld a,h
        xor e
        ld d,a                  ; D = s1^c1
        ld a,h
        and e
        ld e,a                  ; E = s1&c1
        ld a,d
        and l
        or e
        ld e,a                  ; E = carry into bit 2
        ld a,d
        xor l
        ld h,a                  ; H = t1
        ld a,c
        xor e
        ld c,a                  ; C = t2
        ld a,b
        or (ix+0)               ; t0 | self
        and h                   ; & t1
        ld b,a
        ld a,c
        cpl
        and b                   ; & ~t2  -> alive next generation
        ld c,a
        ld a,(colv)
        out (PX),a
        ld a,(rowv)
        out (PY),a
        ld a,c
        out (PD),a
        ld a,(colv)
        inc a
        ld (colv),a
        inc ix
        cp 80
        jp nz,bytelp

        ld hl,cur               ; slide window: cur -> prev, next -> cur
        ld de,prev
        ld bc,164
        ldir
        ld hl,rowv
        inc (hl)
        ld a,(hl)
        cp 240
        jp nz,rowlp
        ret

codeend:
prev:   defs 82
cur:    defs 82
next:   defs 82
save:   defs 82
