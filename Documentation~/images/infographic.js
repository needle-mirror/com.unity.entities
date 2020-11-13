var slideNumber = 1;
advanceSlide(0);
document.getElementById('previous').addEventListener('click', previousSlide);
document.getElementById('next').addEventListener('click', nextSlide);

function nextSlide(evt){
    advanceSlide(1);
}
function previousSlide(evt){
    advanceSlide(-1);
}

function advanceSlide(n) {
    slideNumber += n;
    var slides = document.getElementsByClassName("infographic");
    if (slideNumber > slides.length) 
    {
        slideNumber = 1;
    } 
    else if (slideNumber < 1) 
    {
        slideNumber = slides.length;
    }
    
    for (var i = 0; i < slides.length; i++) 
    {
        slides[i].style.display = "none"; 
    }
    slides[slideNumber - 1].style.display = "block"; 
}